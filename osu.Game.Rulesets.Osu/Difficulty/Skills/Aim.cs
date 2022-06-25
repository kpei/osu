// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using MathNet.Numerics;
using osu.Game.Rulesets.Difficulty.Preprocessing;
using osu.Game.Rulesets.Difficulty.Skills;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Osu.Difficulty.Evaluators;

namespace osu.Game.Rulesets.Osu.Difficulty.Skills
{
    /// <summary>
    /// Represents the skill required to correctly aim at every object in the map with a uniform CircleSize and normalized distances.
    /// </summary>
    public class Aim : Skill
    {
        public Aim(Mod[] mods)
            : base(mods)
        {
        }

        private readonly List<double> aimDifficulties = new List<double>();

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
        /// Calculates the probability of FC'ing the map given a skill level.
        /// </summary>
        /// <param name="skill">
        /// The player's skill level.
        /// </param>
        /// <returns>
        /// The probability of FC'ing a map.
        /// </returns>
        private double getFcProbability(double skill)
        {
            return aimDifficulties.Aggregate<double, double>(1, (current, difficulty) => current * hitProbabilityOf(difficulty, skill));
        }

        /// <summary>
        /// Consider the inverse function of <see cref="getFcProbability"/>. Such a function would take an FC probability as an input
        /// and return the skill level associated with that FC probability. Integrating the inverse from 0 to 1 gives the average
        /// skill level across all probabilities. One can then show that the value of this integral is equivalent to the integral
        /// from 0 to infinity of (1 - <see cref="getFcProbability"/>).
        /// </summary>
        /// <returns>
        /// The mean skill level needed to FC the map.
        /// </returns>
        private double getAimSkillLevel()
        {
            try
            {
                double getNonFcProbability(double skill) => 1 - getFcProbability(skill);
                double skillLevel = 0;

                // MathNet does not support improper integrals, so we will iteratively evaluate the integral from i to i + 1,
                // starting at i = 0, until the integral's value is smaller than tolerance, at which point the loop will break.

                const double tolerance = 1e-6;
                int i = 0;

                while (true)
                {
                    double integral = Integrate.OnClosedInterval(getNonFcProbability, i, i + 1);

                    if (integral < tolerance)
                    {
                        skillLevel += integral;
                        break;
                    }

                    skillLevel += integral;
                    i++;
                }

                return skillLevel;
            }
            catch
            {
                return 0;
            }
        }

        public override void Process(DifficultyHitObject current)
        {
            double aimDifficulty = AimEvaluator.EvaluateDifficultyOf(current);
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
