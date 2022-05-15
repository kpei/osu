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
        private readonly List<double> aimDifficulties = new List<double>();
        private const double fc_probability_threshold = 1 / 2.0;

        public Aim(Mod[] mods)
            : base(mods)
        {
        }

        /// <summary>
        /// Calculates the player's standard deviation on an object if their skill level equals 1, a measure defined as "aim difficulty".
        /// The higher the standard deviation, the more difficult the object is to hit.
        /// Distances are normalized with respect to the radius: 1 distance = 1 radii.
        /// </summary>
        /// <param name="current">
        /// The object's difficulty to calculate.
        /// </param>
        /// <returns>
        /// The difficulty of the object.
        /// </returns>
        private double calculateAimDifficulty(DifficultyHitObject current)
        {
            var osuCurrObj = (OsuDifficultyHitObject)current;
            if (osuCurrObj.BaseObject is Spinner)
                return 0;

            // Aim deviation is proportional to velocity.
            double aimDifficulty = osuCurrObj.LazyJumpDistance / osuCurrObj.StrainTime;

            return aimDifficulty;
        }

        /// <summary>
        /// Calculates the probability of hitting an object with a certain difficulty and skill level.
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

            return SpecialFunctions.Erf(skill / (Math.Sqrt(2) * difficulty));
        }

        /// <summary>
        /// Calculates the probability of FC'ing a map given a skill level and a list of difficulties.
        /// </summary>
        /// <param name="skill">
        /// The player's skill level.
        /// </param>
        /// <param name="difficulties">
        /// List of difficulties to iterate through.
        /// </param>
        /// <returns>
        /// The probability of FC'ing a map.
        /// </returns>
        private double getFcProbability(double skill, IEnumerable<double> difficulties)
        {
            return difficulties.Aggregate<double, double>(1, (current, difficulty) => current * hitProbabilityOf(difficulty, skill));
        }

        /// <summary>
        /// We want to find the skill level such that the probability of FC'ing a map is equal to <see cref="fc_probability_threshold"/>.
        /// Create a function that calculates the probability of FC'ing given a skill level and subtracts <see cref="fc_probability_threshold"/>.
        /// The root of this function is the skill level where the probability of FC'ing the map is <see cref="fc_probability_threshold"/>.
        /// This skill level is defined as the difficulty of the map.
        /// </summary>
        /// <returns>
        /// The skill level such that the probability of FC'ing the map is <see cref="fc_probability_threshold"/>.
        /// </returns>
        private double getAimSkillLevel()
        {
            double fcProbabilityMinusThreshold(double skill) => getFcProbability(skill, aimDifficulties) - fc_probability_threshold;

            // The lower bound must be the skill level such that the probability of hitting the hardest note is fc_probability_threshold.
            double guessLowerBound = SpecialFunctions.ErfInv(fc_probability_threshold) * aimDifficulties.Max() * Math.Sqrt(2);

            // The upper bound must be the skill level such that the probability of hitting every note in the map,
            // assuming each note's difficulty is the same as the difficulty of the hardest note, is fc_probability_threshold.
            double guessUpperBound = SpecialFunctions.ErfInv(Math.Pow(fc_probability_threshold, 1.0 / aimDifficulties.Count)) * aimDifficulties.Max() * Math.Sqrt(2);

            try
            {
                double skillLevel = Brent.FindRoot(fcProbabilityMinusThreshold, guessLowerBound, guessUpperBound);
                return skillLevel;
            }
            catch (NonConvergenceException)
            {
                return 0;
            }
        }

        protected override void Process(DifficultyHitObject current)
        {
            double aimDifficulty = calculateAimDifficulty(current);
            aimDifficulties.Add(aimDifficulty);
        }

        public override double DifficultyValue()
        {
            if (aimDifficulties.Sum() == 0)
                return 0;

            double aimSkillLevel = getAimSkillLevel();
            return aimSkillLevel;
        }
    }
}
