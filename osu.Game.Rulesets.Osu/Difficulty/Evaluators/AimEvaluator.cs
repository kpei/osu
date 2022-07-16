// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

#nullable disable

using System;
using MathNet.Numerics;
using MathNet.Numerics.LinearAlgebra.Double;
using MathNet.Numerics.RootFinding;
using osu.Game.Rulesets.Difficulty.Preprocessing;
using osu.Game.Rulesets.Osu.Difficulty.Preprocessing;

namespace osu.Game.Rulesets.Osu.Difficulty.Evaluators
{
    public static class AimEvaluator
    {
        private const double numerical_algorithm_accuracy = 1e-3;

        /// <summary>
        /// Evaluates the difficulty of successfully aiming at the current object.
        /// </summary>
        public static double EvaluateDifficultyOf(DifficultyHitObject current, double mehHitWindow)
        {
            double aimDifficulty = aimDifficultyOf(current);
            double coordinationDifficulty = coordinationDifficultyOf(current, mehHitWindow);
            return aimDifficulty + coordinationDifficulty;
        }

        /// <summary>
        /// Calculates the aim difficulty of the current object for a player with an aim skill of 1.
        /// </summary>
        private static double aimDifficultyOf(DifficultyHitObject current)
        {
            var osuCurrObj = (OsuDifficultyHitObject)current;

            if (osuCurrObj.Previous(0) == null)
                return 0;

            double integrand(double t) => Math.Sqrt(Math.Pow(velocityVectorAt(osuCurrObj, t)[0], 2) + Math.Pow(velocityVectorAt(osuCurrObj, t)[1], 2));
            return Integrate.OnClosedInterval(integrand, 0, osuCurrObj.StrainTime, numerical_algorithm_accuracy) / osuCurrObj.StrainTime;
        }

        /// <summary>
        /// Calculates the coordination difficulty of the current object, defined as the reciprocal of half of the amount of time the player spends in the note.
        /// </summary>
        private static double coordinationDifficultyOf(DifficultyHitObject current, double mehHitWindow)
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
                    double xComponent(double t) => positionVectorAt(current, t)[0];
                    double yComponent(double t) => positionVectorAt(current, t)[1];

                    // The circle located at (X, Y) with radius r can be described by the equation (x - X)^2 + (y - Y)^2 = r^2.
                    // We can find when the position function intersects the circle by substituting xComponent(t) into x and yComponent(t) into y,
                    // subtracting r^2 from both sides of the equation, and then solving for t.
                    // Because the positions are normalized with respect to the radius, r^2 = 1.
                    double root(double t) => Math.Pow(xComponent(t) - osuCurrObj.NormalizedX, 2) + Math.Pow(yComponent(t) - osuCurrObj.NormalizedY, 2) - 1;

                    double timeEnterNote = Brent.FindRoot(root, 0, osuCurrObj.StrainTime, numerical_algorithm_accuracy);
                    timeInNote += osuCurrObj.StrainTime - timeEnterNote;
                }
            }
            else
            {
                timeInNote += mehHitWindow;
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
                    double xComponent(double t) => positionVectorAt(osuNextObj, t)[0];
                    double yComponent(double t) => positionVectorAt(osuNextObj, t)[1];

                    double root(double t) => Math.Pow(xComponent(t) - osuCurrObj.NormalizedX, 2) + Math.Pow(yComponent(t) - osuCurrObj.NormalizedY, 2) - 1;

                    double timeExitNote = Brent.FindRoot(root, 0, osuNextObj.StrainTime, numerical_algorithm_accuracy);
                    timeInNote += timeExitNote;
                }
            }
            else
            {
                timeInNote += mehHitWindow;
            }

            return 2 / timeInNote;
        }

        /// <summary>
        /// Returns the cursor's position at some time <paramref name="t"/>, where t can range from 0 to this <paramref name="hitObject"/>'s DeltaTime,
        /// based on the path generated by <see cref="positionFunction"/>.
        /// At <paramref name="t"/> = 0, the function returns the coordinates of the previous object.
        /// At <paramref name="t"/> = DeltaTime, the function returns the coordinates of the current object.
        /// </summary>
        private static DenseVector positionVectorAt(DifficultyHitObject hitObject, double t)
        {
            var osuPrevObj = (OsuDifficultyHitObject)hitObject.Previous(0);
            var osuCurrObj = (OsuDifficultyHitObject)hitObject;

            var previousVelocityVector = generateVelocityVectorAt(osuPrevObj);
            var currentVelocityVector = generateVelocityVectorAt(osuCurrObj);

            double ax = osuPrevObj.NormalizedX;
            double bx = osuCurrObj.NormalizedX;

            double ay = osuPrevObj.NormalizedY;
            double by = osuCurrObj.NormalizedY;

            double dt = osuCurrObj.StrainTime;

            double ix = previousVelocityVector[0]; // Velocity along the x direction of the previous object
            double iy = previousVelocityVector[1]; // Velocity along the y direction of the previous object
            double fx = currentVelocityVector[0]; // Velocity along the x direction of the current object
            double fy = currentVelocityVector[1]; // Velocity along the y direction of the current object

            double xComponent = positionFunction(ax, bx, ix, fx, dt, t);
            double yComponent = positionFunction(ay, by, iy, fy, dt, t);

            double[] positionVector = { xComponent, yComponent };
            return new DenseVector(positionVector);
        }

        /// <summary>
        /// Returns the cursor's velocity at some time <paramref name="t"/>, where <paramref name="t"/> can range from 0 to the <paramref name="hitObject"/>'s DeltaTIme.
        /// </summary>
        private static DenseVector velocityVectorAt(DifficultyHitObject hitObject, double t)
        {
            var osuPrevObj = (OsuDifficultyHitObject)hitObject.Previous(0);
            var osuCurrObj = (OsuDifficultyHitObject)hitObject;

            var previousVelocityVector = generateVelocityVectorAt(osuPrevObj);
            var currentVelocityVector = generateVelocityVectorAt(osuCurrObj);

            double ax = osuPrevObj.NormalizedX;
            double bx = osuCurrObj.NormalizedX;

            double ay = osuPrevObj.NormalizedY;
            double by = osuCurrObj.NormalizedY;

            double dt = osuCurrObj.StrainTime;

            double ix = previousVelocityVector[0]; // Velocity along the x direction of the previous object
            double iy = previousVelocityVector[1]; // Velocity along the y direction of the previous object
            double fx = currentVelocityVector[0]; // Velocity along the x direction of the current object
            double fy = currentVelocityVector[1]; // Velocity along the y direction of the current object

            double xComponent = velocityFunction(ax, bx, ix, fx, dt, t);
            double yComponent = velocityFunction(ay, by, iy, fy, dt, t);

            double[] velocityVector = { xComponent, yComponent };
            return new DenseVector(velocityVector);
        }

        /// <summary>
        /// Defines the velocity vector of the <paramref name="hitObject"/> at the time the <paramref name="hitObject"/> is located at.
        /// This vector will determine how the player is moving when they reach the <paramref name="hitObject"/>.
        /// A vector close to (0, 0) means the player almost fully stops at the note (snap aim),
        /// whereas a vector far from (0, 0) indicates the player is moving rapidly through the note (flow aim).
        /// </summary>
        private static DenseVector generateVelocityVectorAt(DifficultyHitObject hitObject)
        {
            double[] velocityVector = { 0.0, 0.0 };
            return new DenseVector(velocityVector);
        }

        /// <summary>
        /// Generates a path along one axis from the previous note to the current one, and returns the cursor's location at a time <paramref name="t"/>,
        /// ranging from 0 to the current object's delta time.
        /// </summary>
        /// <param name="x0">
        /// Location of the previous note.
        /// </param>
        /// <param name="x1">
        /// Location of the current note.
        /// </param>
        /// <param name="v0">
        /// Velocity at the previous note.
        /// </param>
        /// <param name="v1">
        /// Velocity at the current note.
        /// </param>
        /// <param name="dt">
        /// Time between the current and previous notes.
        /// </param>
        /// <param name="t">
        /// Arbitrary time t.
        /// </param>
        private static double positionFunction(double x0, double x1, double v0, double v1, double dt, double t)
        {
            const double pi = Math.PI;
            return t * t * (v1 - v0) / (2 * dt) - Math.Cos(pi * t / dt) * ((x1 - x0) / 2 - dt * (v0 + v1) / 4) + t * v0 - dt * (v0 + v1) / 4 + (x0 + x1) / 2;
        }

        /// <summary>
        /// The derivative of the path generated by <see cref="positionFunction"/>. Returns the velocity at a time <paramref name="t"/>,
        /// ranging from 0 to the current object's delta time.
        /// </summary>
        /// <param name="x0">
        /// Location of the previous note.
        /// </param>
        /// <param name="x1">
        /// Location of the current note.
        /// </param>
        /// <param name="v0">
        /// Velocity at the previous note.
        /// </param>
        /// <param name="v1">
        /// Velocity at the current note.
        /// </param>
        /// <param name="dt">
        /// Time between the current and previous notes.
        /// </param>
        /// <param name="t">
        /// Arbitrary time t.
        /// </param>
        private static double velocityFunction(double x0, double x1, double v0, double v1, double dt, double t)
        {
            const double pi = Math.PI;
            return v0 + t * (v1 - v0) / dt - pi * Math.Sin(pi * t / dt) * (dt * (v0 + v1) + 2 * (x0 - x1)) / (4 * dt);
        }
    }
}
