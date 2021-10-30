// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using MathNet.Numerics;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using MathNet.Numerics.RootFinding;
using osu.Game.Rulesets.Difficulty.Preprocessing;
using osu.Game.Rulesets.Difficulty.Skills;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Osu.Difficulty.Preprocessing;
using osu.Game.Rulesets.Osu.Objects;

namespace osu.Game.Rulesets.Osu.Difficulty.Skills
{
    /// <summary>
    /// Represents the skill required to correctly aim at every object in the map.
    /// </summary>
    public class Aim : Skill
    {
        protected override int HistoryLength => 2;
        private readonly double mehHitWindow;
        private readonly double clockRate;
        private readonly List<double> difficulties = new List<double>();

        public Aim(Mod[] mods, double mehHitWindow, double clockRate)
            : base(mods)
        {
            this.mehHitWindow = mehHitWindow;
            this.clockRate = clockRate;
        }

        private double difficultyValueOf(DifficultyHitObject current)
        {
            var currentObject = (OsuDifficultyHitObject)current;
            var currentObjectBase = (OsuHitObject)currentObject.BaseObject;

            var lastObject = Previous.Count > 0 ? (OsuDifficultyHitObject)Previous[0] : null;
            var secondLastObject = Previous.Count > 1 ? (OsuDifficultyHitObject)Previous[1] : null;

            OsuDifficultyHitObject nextObject = null;

            if (lastObject != null && currentObject.nextObject != null)
            {
                nextObject = new OsuDifficultyHitObject(null, currentObject.nextObject, (OsuHitObject)lastObject.BaseObject, currentObjectBase, clockRate);
            }

            double radius = currentObjectBase.Radius;

            if (currentObject.BaseObject is Spinner)
                return 0;

            // Add extra time to deltaTime for cheesing corrections.
            double extraDeltaTime = 0;

            /*
             * Correction #1: Early taps.
             * The player can tap the current note early if the previous deltaTime is greater than the current deltaTime.
             * This kind of cheesing gives the player extra time to hit the current pattern.
             * The maximum amount of extra time is the 50 hit window or the time difference, whichever is lower.
             */

            if (secondLastObject == null)
            {
                extraDeltaTime += mehHitWindow;
            }
            else
            {
                Debug.Assert(lastObject != null, nameof(lastObject) + " != null");
                double deltaTime = currentObject.StartTime - lastObject.StartTime;
                double lastDeltaTime = lastObject.StartTime - secondLastObject.StartTime;
                double timeDifference = lastDeltaTime - deltaTime;

                if (timeDifference > 0)
                {
                    extraDeltaTime += Math.Min(mehHitWindow, timeDifference);
                }
            }

            if (nextObject == null)
            {
                extraDeltaTime += mehHitWindow;
            }
            else
            {
                double nextDeltaTime = nextObject.StartTime - currentObject.StartTime;
                double deltaTime = currentObject.StartTime - lastObject.StartTime;
                double timeDifference = nextDeltaTime - deltaTime;

                if (timeDifference > 0)
                {
                    extraDeltaTime += Math.Min(mehHitWindow, timeDifference);
                }
            }

            extraDeltaTime = Math.Min(mehHitWindow, extraDeltaTime);
            double effectiveDeltaTime = currentObject.DeltaTime + extraDeltaTime;

            double difficulty;
            const double deviation_intercept = 100;

            if (currentObject.JumpDistance >= 2 * radius)
            {
                difficulty = (currentObject.JumpDistance + currentObject.TravelDistance + deviation_intercept) / effectiveDeltaTime;
            }
            else
            {
                difficulty = (currentObject.JumpDistance + currentObject.TravelDistance) * (2 * radius + deviation_intercept) / (2 * radius * effectiveDeltaTime);
            }

            return difficulty / radius;
        }

        private double hitProbabilityOf(double difficulty, double skill)
        {
            if (difficulty == 0)
                return 1;

            if (skill == 0)
                return 0;

            return SpecialFunctions.Erf(skill / (Math.Sqrt(2) * difficulty));
        }

        private double getExpectedHits(double skill)
        {
            double expectedHits = difficulties.Sum(t => hitProbabilityOf(t, skill));
            return expectedHits;
        }

        protected override void Process(DifficultyHitObject current)
        {
            double difficulty = difficultyValueOf(current);
            difficulties.Add(difficulty);
        }

        public override double DifficultyValue()
        {
            const double guess_lower_bound = 0;
            const double guess_upper_bound = 1;

            double expectedHitsMinusThreshold(double skill)
            {
                const int threshold = 1;
                double expectedHits = getExpectedHits(skill);
                double result = difficulties.Count - expectedHits - threshold;
                return result;
            }

            try
            {
                double skillLevel = Bisection.FindRootExpand(expectedHitsMinusThreshold, guess_lower_bound, guess_upper_bound);
                return skillLevel;
            }
            catch (NonConvergenceException)
            {
                return double.PositiveInfinity;
            }
        }
    }
}
