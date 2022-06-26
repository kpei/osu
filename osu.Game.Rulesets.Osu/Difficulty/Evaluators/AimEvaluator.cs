// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

#nullable disable

using System;
using MathNet.Numerics.RootFinding;
using osu.Game.Rulesets.Difficulty.Preprocessing;
using osu.Game.Rulesets.Osu.Difficulty.Preprocessing;
using osu.Game.Rulesets.Osu.Objects;

namespace osu.Game.Rulesets.Osu.Difficulty.Evaluators
{
    public static class AimEvaluator
    {
        public static double EvaluateDifficultyOf(DifficultyHitObject current)
        {
            var osuCurrObj = (OsuDifficultyHitObject)current;
            if (osuCurrObj.BaseObject is Spinner)
                return 0;

            double aimDifficulty = aimDifficultyOf(current);
            double coordinationDifficulty = coordinationDifficultyOf(current);
            return aimDifficulty + coordinationDifficulty;
        }

        /// <summary>
        /// Calculates the aim difficulty of the current object for a player with an aim deviation of 1.
        /// </summary>
        /// <param name="current">
        /// The current object.
        /// </param>
        /// <returns>
        /// The aim difficulty of the current object.
        /// </returns>
        private static double aimDifficultyOf(DifficultyHitObject current)
        {
            var osuCurrObj = (OsuDifficultyHitObject)current;
            return osuCurrObj.MinimumJumpDistance / osuCurrObj.StrainTime; // Aim difficulty is proportional to velocity.
        }

        /// <summary>
        /// Calculates the coordination difficulty of the current object.
        /// The coordination difficulty is the reciprocal of half of the amount of time the player spends in the note.
        /// </summary>
        /// <param name="current">
        /// The current object.
        /// </param>
        /// <returns>
        /// The coordination difficulty of the current object.
        /// </returns>
        private static double coordinationDifficultyOf(DifficultyHitObject current)
        {
            var osuCurrObj = (OsuDifficultyHitObject)current;
            var osuNextObj = (OsuDifficultyHitObject)current.Next(0);

            if (osuCurrObj.BaseObject is Spinner)
                return 0;

            double timeInCurrentNote = 0;

            // Determine the position function from the previous note to the current note.
            // Determine when the position function equals LazyJumpDistance - 1, which is the time that the player enters the note.
            // If this number is subtracted from DeltaTime, we get the amount of time the cursor is in the note as it moves from the previous note to the current note.
            // This time is equivalent to finding when the position function equals 1.

            if (osuCurrObj.LazyJumpDistance > 1)
            {
                double currentPositionFunctionMinusOne(double time) => positionFunction(osuCurrObj.LazyJumpDistance, osuCurrObj.StrainTime, 0, 0, time) - 1;
                double timeEnterNote = Brent.FindRoot(currentPositionFunctionMinusOne, 1e-6, osuCurrObj.StrainTime, 1e-4);
                timeInCurrentNote += timeEnterNote;
            }
            else
            {
                // If the current and previous objects are overlapped by 50% of more, just add the DeltaTime of the current object.
                timeInCurrentNote += osuCurrObj.StrainTime;
            }

            // We also need to take into account that fact that the player leaves the current note as well.
            // As the player leaves the current note and moves to the next note, the player spends time in the current note as well.
            // This time is calculated and added to the amount of time spent in the current note.

            if (osuNextObj != null)
            {
                if (osuNextObj.LazyJumpDistance > 1)
                {
                    double nextPositionFunctionMinusOne(double time) => positionFunction(osuNextObj.LazyJumpDistance, osuNextObj.StrainTime, 0, 0, time) - 1;
                    double timeExitNote = Brent.FindRoot(nextPositionFunctionMinusOne, 1e-6, osuNextObj.StrainTime, 1e-4);
                    timeInCurrentNote += timeExitNote;
                }
                else
                {
                    timeInCurrentNote += osuNextObj.StrainTime;
                }
            }
            else
            {
                timeInCurrentNote += 200;
            }

            // If the amount of time spent in the note is t, then this is approximately the same as a hit window of ± t / 2.
            double hitWindow = timeInCurrentNote / 2;

            return 1 / hitWindow;
        }

        /// <summary>
        /// Gives the cursor's position at time <paramref name="t"/>. Returns 0 when <paramref name="t"/> = 0, and returns <paramref name="distance"/> when <paramref name="t"/> = <paramref name="deltaTime"/>.
        /// The cursor begins with an velocity of <paramref name="initialVelocity"/> at <paramref name="t"/> = 0, and ends with a velocity of
        /// <paramref name="finalVelocity"/> at <paramref name="t"/> = <paramref name="deltaTime"/>.
        /// </summary>
        /// <param name="distance">
        /// How far the cursor moves.
        /// </param>
        /// <param name="deltaTime">
        /// How much time the cursor has to move.
        /// </param>
        /// <param name="initialVelocity">
        /// The cursor's velocity when <paramref name="t"/> = 0.
        /// </param>
        /// <param name="finalVelocity">
        ///The cursor's velocity when <paramref name="t"/> = <paramref name="deltaTime"/>.
        /// </param>
        /// <param name="t">
        /// Any time between 0 and <paramref name="deltaTime"/>.
        /// </param>
        /// <returns>
        /// The cursor's position at time <paramref name="t"/>.
        /// </returns>
        private static double positionFunction(double distance, double deltaTime, double initialVelocity, double finalVelocity, double t)
        {
            return 6 * distance * Math.Pow(t, 5) / Math.Pow(deltaTime, 5) -
                   15 * distance * Math.Pow(t, 4) / Math.Pow(deltaTime, 4) +
                   10 * distance * Math.Pow(t, 3) / Math.Pow(deltaTime, 3) -
                   3 * finalVelocity * Math.Pow(t, 5) / Math.Pow(deltaTime, 4) +
                   7 * finalVelocity * Math.Pow(t, 4) / Math.Pow(deltaTime, 3) -
                   4 * finalVelocity * Math.Pow(t, 3) / Math.Pow(deltaTime, 2) -
                   3 * initialVelocity * Math.Pow(t, 5) / Math.Pow(deltaTime, 4) +
                   8 * initialVelocity * Math.Pow(t, 4) / Math.Pow(deltaTime, 3) -
                   6 * initialVelocity * Math.Pow(t, 3) / Math.Pow(deltaTime, 2) +
                   initialVelocity * t;
        }
    }
}
