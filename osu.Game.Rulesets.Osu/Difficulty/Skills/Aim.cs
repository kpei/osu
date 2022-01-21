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

namespace osu.Game.Rulesets.Osu.Difficulty.Skills
{
    /// <summary>
    /// Represents the skill required to correctly aim at every object in the map.
    /// </summary>
    public class Aim : Skill
    {
        private readonly double radius;
        private readonly List<(double, double)> difficulties = new List<(double, double)>();
        private const double miss_count_threshold = 0.5;

        public Aim(Mod[] mods, double radius)
            : base(mods)
        {
            this.radius = radius;
        }

        private double strain;

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
        private (double, double) difficultyValueOf(DifficultyHitObject current)
        {
            var currentObject = (OsuDifficultyHitObject)current;
            var previousObject = Previous.Count > 0 ? (OsuDifficultyHitObject)Previous[0] : null;

            if (currentObject.BaseObject is Spinner)
                return (0, 0);

            double xDifficulty = (currentObject.JumpDistance + currentObject.TravelDistance) / currentObject.DeltaTime;

            if (currentObject.Angle != null && previousObject != null)
            {
                double angle = currentObject.Angle.Value;
                xDifficulty *= 1 + angle / Math.PI;
            }

            strain += xDifficulty;
            strain *= 0.75;

            if (currentObject.DeltaTime > 1000)
            {
                strain = 0;
            }

            return (strain, 0);
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

        private double getExpectedMisses(double skill)
        {
            double expectedMisses = 0;

            foreach ((double xDifficulty, double yDifficulty) in difficulties)
            {
                expectedMisses += 1 - hitProbabilityOf(xDifficulty, skill) * hitProbabilityOf(yDifficulty, skill);
            }

            return expectedMisses;
        }

        private double expectedMissesMinusThreshold(double skill) => getExpectedMisses(skill) - miss_count_threshold;

        protected override void Process(DifficultyHitObject current)
        {
            (double, double) difficulty = difficultyValueOf(current);
            difficulties.Add(difficulty);
        }

        public override double DifficultyValue()
        {
            const double guess_lower_bound = 0.0;
            const double guess_upper_bound = 2.0;

            try
            {
                // Find the skill level so that the probability of FC'ing is the threshold.
                double skillLevel = Bisection.FindRootExpand(expectedMissesMinusThreshold, guess_lower_bound, guess_upper_bound);
                return skillLevel;
            }
            catch (NonConvergenceException)
            {
                return double.PositiveInfinity;
            }
        }
    }
}
