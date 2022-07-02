// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

#nullable disable

using System;
using MathNet.Numerics.LinearAlgebra.Double;
using MathNet.Numerics.RootFinding;
using osu.Game.Rulesets.Difficulty.Preprocessing;
using osu.Game.Rulesets.Osu.Difficulty.Preprocessing;

namespace osu.Game.Rulesets.Osu.Difficulty.Evaluators
{
    public static class AimEvaluator
    {
        private const double root_finding_accuracy = 1e-4;

        /// <summary>
        /// Evaluates the difficulty of successfully aiming at the current object.
        /// </summary>
        public static double EvaluateDifficultyOf(DifficultyHitObject current)
        {
            double aimDifficulty = aimDifficultyOf(current);
            double coordinationDifficulty = coordinationDifficultyOf(current);
            return aimDifficulty + coordinationDifficulty;
        }

        /// <summary>
        /// Calculates the aim difficulty of the current object for a player with an aim skill of 1.
        /// </summary>
        private static double aimDifficultyOf(DifficultyHitObject current)
        {
            var osuCurrObj = (OsuDifficultyHitObject)current;
            return osuCurrObj.MinimumJumpDistance / osuCurrObj.StrainTime; // Aim difficulty is proportional to velocity.
        }

        /// <summary>
        /// Calculates the coordination difficulty of the current object, defined as the reciprocal of half of the amount of time the player spends in the note.
        /// </summary>
        private static double coordinationDifficultyOf(DifficultyHitObject current)
        {
            var osuPrevObj = (OsuDifficultyHitObject)current.Previous(0);
            var osuCurrObj = (OsuDifficultyHitObject)current;
            var osuNextObj = (OsuDifficultyHitObject)current.Next(0);

            double timeInNote = 0;

            // Determine the amount of time the cursor is within the current circle as it moves from the previous circle.
            if (osuPrevObj != null)
            {
                // This distance must be computed because sliders aren't being taken into account.
                double realSquaredDistance = Math.Pow(osuCurrObj.NormalizedX - osuPrevObj.NormalizedX, 2) + Math.Pow(osuCurrObj.NormalizedY - osuPrevObj.NormalizedY, 2);

                // If the current and previous objects are overlapped by 50% or more, just add the DeltaTime of the current object.
                if (realSquaredDistance <= 1)
                {
                    timeInNote += osuCurrObj.StrainTime;
                }
                else
                {
                    double xComponent(double t) => positionVectorOf(current, t)[0];
                    double yComponent(double t) => positionVectorOf(current, t)[1];

                    // The circle located at (X, Y) with radius r can be described by the equation (x - X)^2 + (y - Y)^2 = r^2.
                    // We can find when the position function intersects the circle by substituting xComponent(t) into x and yComponent(t) into y,
                    // subtracting r^2 from both sides of the equation, and then solving for t.
                    // Because the positions are normalized with respect to the radius, r^2 = 1.
                    double root(double t) => Math.Pow(xComponent(t) - osuCurrObj.NormalizedX, 2) + Math.Pow(yComponent(t) - osuCurrObj.NormalizedY, 2) - 1;

                    double timeEnterNote = Brent.FindRoot(root, 0, osuCurrObj.StrainTime, root_finding_accuracy);
                    timeInNote += osuCurrObj.StrainTime - timeEnterNote;
                }
            }

            // Determine the amount of time the cursor is within the current circle as it moves toward the next circle.
            if (osuNextObj != null)
            {
                double realSquaredDistance = Math.Pow(osuNextObj.NormalizedX - osuCurrObj.NormalizedX, 2) + Math.Pow(osuNextObj.NormalizedY - osuCurrObj.NormalizedY, 2);

                if (realSquaredDistance <= 1)
                {
                    timeInNote += osuNextObj.StrainTime;
                }
                else
                {
                    double xComponent(double t) => positionVectorOf(osuNextObj, t)[0];
                    double yComponent(double t) => positionVectorOf(osuNextObj, t)[1];

                    double root(double t) => Math.Pow(xComponent(t) - osuCurrObj.NormalizedX, 2) + Math.Pow(yComponent(t) - osuCurrObj.NormalizedY, 2) - 1;

                    double timeExitNote = Brent.FindRoot(root, 0, osuNextObj.StrainTime, root_finding_accuracy);
                    timeInNote += timeExitNote;
                }
            }

            if (timeInNote == 0)
                return 0;

            return 2 / timeInNote;
        }

        /// <summary>
        /// A path from the previous <paramref name="hitObject"/> to the current <paramref name="hitObject"/> is generated, which the cursor is assumed to follow.
        /// This function returns the cursor's position at any time <paramref name="t"/>, where t can range from 0 to this <paramref name="hitObject"/>'s DeltaTime.
        /// At <paramref name="t"/> = 0, the function returns the coordinates of the previous object.
        /// At <paramref name="t"/> = DeltaTime, the function returns the coordinates of the current object.
        /// </summary>
        private static DenseVector positionVectorOf(DifficultyHitObject hitObject, double t)
        {
            var osuPrevObj = (OsuDifficultyHitObject)hitObject.Previous(0);
            var osuCurrObj = (OsuDifficultyHitObject)hitObject;

            var previousVelocityVector = generateVelocityVectorOf(osuPrevObj);
            var currentVelocityVector = generateVelocityVectorOf(osuCurrObj);

            double ax = osuPrevObj.NormalizedX;
            double bx = osuCurrObj.NormalizedX;

            double ay = osuPrevObj.NormalizedY;
            double by = osuCurrObj.NormalizedY;

            double dt = osuCurrObj.StrainTime;

            double ix = previousVelocityVector[0]; // Velocity along the x direction of the previous object
            double iy = previousVelocityVector[1]; // Velocity along the y direction of the previous object
            double fx = currentVelocityVector[0]; // Velocity along the x direction of the current object
            double fy = currentVelocityVector[1]; // Velocity along the y direction of the current object

            double xComponent = (-Math.Pow(t - dt, 3) * (6 * t * t + 3 * t * dt + dt * dt) * ax + Math.Pow(t, 3) * (6 * t * t - 15 * t * dt + 10 * dt * dt) * bx - t * (t - dt) * dt * (t * t * (3 * t - 4 * dt) * fx + Math.Pow(t - dt, 2) * (3 * t + dt) * ix))
                                / Math.Pow(dt, 5);
            double yComponent = (-Math.Pow(t - dt, 3) * (6 * t * t + 3 * t * dt + dt * dt) * ay + Math.Pow(t, 3) * (6 * t * t - 15 * t * dt + 10 * dt * dt) * by - t * (t - dt) * dt * (t * t * (3 * t - 4 * dt) * fy + Math.Pow(t - dt, 2) * (3 * t + dt) * iy))
                                / Math.Pow(dt, 5);

            double[] positionVector = { xComponent, yComponent };
            return new DenseVector(positionVector);
        }

        /// <summary>
        /// Returns the velocity vector of the <paramref name="hitObject"/> at the time the <paramref name="hitObject"/> is located at.
        /// </summary>
        private static DenseVector generateVelocityVectorOf(DifficultyHitObject hitObject)
        {
            double[] velocityVector = { 0.0, 0.0 };
            return new DenseVector(velocityVector);
        }
    }
}
