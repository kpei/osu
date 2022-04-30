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
        private readonly List<double> tapDifficulties = new List<double>();
        private const double fc_probability_threshold = 1 / 15.0;

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

        private double initialVelocity;
        private double finalVelocity;

        /// <summary>
        /// Calculates how long the player stays on a note by finding the path of minimum jerk (quintic)
        /// given the initial velocity (player's velocity at the end of the previous note)
        /// and final velocity (player's velocity at the end of the this note).
        /// Initial and final velocities can range from 0 to distance / time.
        /// The difficulty is the reciprocal of the time the player stays on the note.
        /// </summary>
        /// <param name="current">
        /// The object's difficulty to calculate.
        /// </param>
        /// <returns>
        /// The "tap precision" of the note, which is the reciprocal of the amount of time spent on the note.
        /// </returns>
        private double calculateTapPrecision(DifficultyHitObject current)
        {
            var osuCurrObj = (OsuDifficultyHitObject)current;
            double radius = ((OsuHitObject)osuCurrObj.BaseObject).Radius;

            if (osuCurrObj.BaseObject is Spinner)
                return 0;

            // Assume that the player is already on the note if the notes are 50% overlapped.
            if (osuCurrObj.LazyJumpDistance < 1)
            {
                return 1 / osuCurrObj.StrainTime;
            }

            double rawDistance = osuCurrObj.LazyJumpDistance * radius;
            double deltaTime = osuCurrObj.StrainTime;

            // Currently set to zero, meaning snap aim is assumed.
            // Eventually, when the tendency for a player to flow aim is looked into, this will be updated to a function.
            finalVelocity = 0;

            double a3 = (10 * rawDistance - 4 * deltaTime * finalVelocity - 6 * deltaTime * initialVelocity) / Math.Pow(deltaTime, 3);
            double a4 = (-15 * rawDistance + 7 * deltaTime * finalVelocity + 8 * deltaTime * initialVelocity) / Math.Pow(deltaTime, 4);
            double a5 = (6 * rawDistance - 3 * deltaTime * (finalVelocity + initialVelocity)) / Math.Pow(deltaTime, 5);

            double position(double t) => initialVelocity * t + a3 * t * t * t + a4 * t * t * t * t + a5 * t * t * t * t * t;
            double positionMinusRadius(double t) => position(t) - (rawDistance - radius);

            double root = Brent.FindRoot(positionMinusRadius, 0, osuCurrObj.DeltaTime);

            initialVelocity = finalVelocity;

            double timeInCircle = deltaTime - root;
            return 1 / timeInCircle;
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

            const double guess_lower_bound = 0;
            const double guess_upper_bound = 1;

            try
            {
                double skillLevel = Brent.FindRootExpand(fcProbabilityMinusThreshold, guess_lower_bound, guess_upper_bound);
                return skillLevel;
            }
            catch (NonConvergenceException)
            {
                return 0;
            }
        }

        /// <summary>
        /// We want to find the tap skill required such that the probability that the player always taps when the cursor
        /// is on top of the circle (assume perfect aim) is <see cref="fc_probability_threshold"/>.
        /// </summary>
        /// <returns></returns>
        private double getTapSkillLevel()
        {
            double fcProbabilityMinusThreshold(double skill) => getFcProbability(skill, tapDifficulties) - fc_probability_threshold;

            const double guess_lower_bound = 0;
            const double guess_upper_bound = 1;

            try
            {
                double skillLevel = Brent.FindRootExpand(fcProbabilityMinusThreshold, guess_lower_bound, guess_upper_bound);
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

            double tapDifficulty = calculateTapPrecision(current);
            tapDifficulties.Add(tapDifficulty);
        }

        public override double DifficultyValue()
        {
            if (aimDifficulties.Sum() == 0)
                return 0;

            double aimSkillLevel = getAimSkillLevel();
            double tapSkillLevel = getTapSkillLevel();

            double skillLevel = aimSkillLevel + tapSkillLevel;
            return skillLevel;
        }
    }
}
