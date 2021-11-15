// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using MathNet.Numerics;
using System;
using System.Collections.Generic;
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
        private readonly double radius;
        private readonly double greatHitWindow;
        private readonly List<double> difficulties = new List<double>();

        public Aim(Mod[] mods, double radius, double greatHitWindow)
            : base(mods)
        {
            this.radius = radius;
            this.greatHitWindow = greatHitWindow;
        }

        /// <summary>
        /// Calculates the player's standard deviation on an object if their skill level equals 1.
        /// The higher the standard deviation, the more difficult the object is to hit.
        /// </summary>
        /// <param name="current">
        /// The object's difficulty to calculate.
        /// </param>
        /// <returns>
        /// The difficulty of the object.
        /// </returns>
        private double difficultyValueOf(DifficultyHitObject current)
        {
            var currentObject = (OsuDifficultyHitObject)current;

            if (currentObject.BaseObject is Spinner)
                return 0;

            double extraDeltaTime = 0;

            if (Previous.Count > 0)
            {
                if (Previous[0].DeltaTime > currentObject.DeltaTime)
                {
                    double timeDifference = Previous[0].DeltaTime - currentObject.DeltaTime;
                    extraDeltaTime += Math.Min(greatHitWindow, timeDifference);
                }
            }
            else
            {
                extraDeltaTime += greatHitWindow;
            }

            extraDeltaTime = Math.Min(greatHitWindow, extraDeltaTime);
            double effectiveDeltaTime = currentObject.DeltaTime + extraDeltaTime;

            double difficulty = (currentObject.JumpDistance + currentObject.TravelDistance) / effectiveDeltaTime;

            return difficulty;
        }

        /// <summary>
        /// Calculates the probability of hitting an object with a certain difficulty and skill level.
        /// The player's hits follow a normal distribution, so the CDF of the normal distribution is used.
        /// </summary>
        /// <param name="difficulty">
        /// The difficulty of the object.
        /// </param>
        /// <param name="skill">
        /// The player's skill level.
        /// </param>
        /// <returns>
        /// The probability of successfully hitting the object.
        /// </returns>
        private double hitProbabilityOf(double difficulty, double skill)
        {
            if (difficulty == 0)
                return 1;

            if (skill == 0)
                return 0;

            return SpecialFunctions.Erf(radius * skill / (Math.Sqrt(2) * difficulty));
        }

        /// <summary>
        /// Calculates the expected number of objects the player will successfully hit on a map given a skill level.
        /// </summary>
        /// <param name="skill">
        /// The player's skill level.
        /// </param>
        /// <returns>
        /// The expected number of objects the player will successfully hit.
        /// </returns>
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
            const double guess_lower_bound = 0.0;
            const double guess_upper_bound = 2.0;

            double expectedHitsMinusThreshold(double skill)
            {
                const double threshold = 4;
                double expectedHits = getExpectedHits(skill);
                double result = difficulties.Count - expectedHits - threshold;
                return result;
            }

            try
            {
                // Find the skill level so that the expected number of misses is 1.
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
