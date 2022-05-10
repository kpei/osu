// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using MathNet.Numerics;
using MathNet.Numerics.RootFinding;
using osu.Game.Rulesets.Difficulty.Preprocessing;
using osu.Game.Rulesets.Difficulty.Skills;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Osu.Difficulty.Preprocessing;
using osu.Game.Rulesets.Osu.Objects;

namespace osu.Game.Rulesets.Osu.Difficulty.Skills
{
    public class Coordination : Skill
    {
        private readonly List<double> coordinationDifficulties = new List<double>();
        private const double fc_probability_threshold = 1 / 2.0;

        public Coordination(Mod[] mods)
            : base(mods)
        {
        }

        private double initialVelocity;
        private double finalVelocity;

        /// <summary>
        /// Calculates how long the player stays on a note by finding the path of minimum jerk (quintic)
        /// given the initial velocity (player's velocity at the end of the previous note)
        /// and final velocity (player's velocity at the end of the this note).
        /// Initial and final velocities can range from 0 to distance / time.
        /// The coordination difficulty is the reciprocal of the time the player stays on the note.
        /// </summary>
        /// <param name="current">
        /// The object's difficulty to calculate.
        /// </param>
        /// <returns>
        /// The tap coordination difficulty of the note, which is the reciprocal of the amount of time spent on the note.
        /// </returns>
        private double calculateCoordinationDifficulty(DifficultyHitObject current)
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

            double root = Brent.FindRoot(positionMinusRadius, 0, osuCurrObj.StrainTime);

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
        /// We want to find the coordination skill required such that the probability that the player always taps when the cursor
        /// is on top of the circle (assume perfect aim) is <see cref="fc_probability_threshold"/>.
        /// </summary>
        /// <returns>
        /// The coordination skill required.
        /// </returns>
        private double getCoordinationSkillLevel()
        {
            double fcProbabilityMinusThreshold(double skill) => getFcProbability(skill, coordinationDifficulties) - fc_probability_threshold;

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
            double coordinationDifficulty = calculateCoordinationDifficulty(current);
            coordinationDifficulties.Add(coordinationDifficulty);
        }

        public override double DifficultyValue()
        {
            if (coordinationDifficulties.Sum() == 0)
                return 0;

            double coordinationSkillLevel = getCoordinationSkillLevel();
            return coordinationSkillLevel;
        }
    }
}
