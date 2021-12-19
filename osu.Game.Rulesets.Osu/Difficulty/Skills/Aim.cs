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

        public Aim(Mod[] mods, double radius)
            : base(mods)
        {
            this.radius = radius;
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
        private (double, double) difficultyValueOf(DifficultyHitObject current)
        {
            var currentObject = (OsuDifficultyHitObject)current;

            if (currentObject.BaseObject is Spinner)
                return (0, 0);

            double xDifficulty = (currentObject.JumpDistance + currentObject.TravelDistance) / currentObject.DeltaTime;
            double yDifficulty = 0;

            return (xDifficulty, yDifficulty);
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
        /// Calculates the probability a player will FC the map given a skill level.
        /// </summary>
        /// <param name="skill">
        /// The player's skill level.
        /// </param>
        /// <returns>
        /// The probability of FC'ing the map.
        /// </returns>
        private double getFcProbability(double skill)
        {
            double fcProbability = 1;

            foreach ((double xDifficulty, double yDifficulty) in difficulties)
            {
                fcProbability *= hitProbabilityOf(xDifficulty, skill) * hitProbabilityOf(yDifficulty, skill);
            }

            return fcProbability;
        }

        protected override void Process(DifficultyHitObject current)
        {
            (double, double) difficulty = difficultyValueOf(current);
            difficulties.Add(difficulty);
        }

        public override double DifficultyValue()
        {
            const double guess_lower_bound = 0.0;
            const double guess_upper_bound = 2.0;

            double fcProbabilityMinusThreshold(double skill)
            {
                const double threshold = 0.01;
                double fcProbability = getFcProbability(skill);
                return fcProbability - threshold;
            }

            try
            {
                // Find the skill level so that the probability of FC'ing is the threshold.
                double skillLevel = Bisection.FindRootExpand(fcProbabilityMinusThreshold, guess_lower_bound, guess_upper_bound);
                return skillLevel;
            }
            catch (NonConvergenceException)
            {
                return double.PositiveInfinity;
            }
        }
    }
}
