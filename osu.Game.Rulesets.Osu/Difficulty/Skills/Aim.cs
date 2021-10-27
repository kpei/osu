// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using MathNet.Numerics;
using System;
using System.Collections.Generic;
using MathNet.Numerics.RootFinding;
using osu.Game.Rulesets.Difficulty.Preprocessing;
using osu.Game.Rulesets.Difficulty.Skills;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Osu.Difficulty.Preprocessing;
using osu.Game.Rulesets.Osu.Objects;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.Osu.Difficulty.Skills
{
    /// <summary>
    /// Represents the skill required to correctly aim at every object in the map.
    /// </summary>
    public class Aim : Skill
    {
        protected override int HistoryLength => 1;

        private double probabilityToHit(DifficultyHitObject current, double skill)
        {
            var currentObject = (OsuDifficultyHitObject)current;
            var currentObjectBase = (OsuHitObject)currentObject.BaseObject;

            double mehHitWindow = currentObjectBase.HitWindows.WindowFor(HitResult.Meh);
            double radius = currentObjectBase.Radius;

            if (skill == 0)
                return 0;

            if (currentObject.JumpDistance == 0 || currentObject.BaseObject is Spinner)
                return 1;

            double xDeviation;

            const double deviation_intercept = 100;

            if (currentObject.JumpDistance >= 2 * radius)
            {
                xDeviation = (currentObject.JumpDistance + deviation_intercept) / (skill * currentObject.DeltaTime);
            }
            else
            {
                xDeviation = currentObject.JumpDistance * (2 * radius + deviation_intercept) / (2 * radius * skill * currentObject.DeltaTime);
            }

            /*
             * To compute the exact hit probability, a definite integral algorithm is required.
             * This definite algorithm is too slow for our needs, even with low precision.
             * So, we will approximate by multiplying two normal CDFs.
             * This has the effect of treating circles as squares, but it's a good, extremely fast approximation.
             */

            double xHitProbability = SpecialFunctions.Erf(radius / (Math.Sqrt(2) * xDeviation));
            return xHitProbability;
        }

        public Aim(Mod[] mods)
            : base(mods)
        {
        }

        private readonly List<DifficultyHitObject> difficultyHitObjects = new List<DifficultyHitObject>();

        protected override void Process(DifficultyHitObject current)
        {
            difficultyHitObjects.Add(current);
        }

        private double getExpectedHits(double skill)
        {
            double hits = 0;

            for (int i = 0; i < difficultyHitObjects.Count; i++)
            {
                var current = difficultyHitObjects[i];
                double hitProbability = probabilityToHit(current, skill);
                hits += hitProbability;
            }

            return hits;
        }

        public override double DifficultyValue()
        {
            const double guess_lower_bound = 0;
            const double guess_upper_bound = 1;

            double expectedHitsMinusThreshold(double skill)
            {
                const int threshold = 1;
                double expectedHits = getExpectedHits(skill);
                double result = difficultyHitObjects.Count - expectedHits - threshold;
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
