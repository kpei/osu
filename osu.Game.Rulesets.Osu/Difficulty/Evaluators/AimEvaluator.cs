// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

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

        private static double aimDifficultyOf(DifficultyHitObject current)
        {
            var osuCurrObj = (OsuDifficultyHitObject)current;
            return osuCurrObj.MinimumJumpDistance / osuCurrObj.StrainTime;
        }

        private static double coordinationDifficultyOf(DifficultyHitObject current)
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
            const double initial_velocity = 0;
            const double final_velocity = 0;

            double a3 = (10 * rawDistance - 4 * deltaTime * final_velocity - 6 * deltaTime * initial_velocity) / Math.Pow(deltaTime, 3);
            double a4 = (-15 * rawDistance + 7 * deltaTime * final_velocity + 8 * deltaTime * initial_velocity) / Math.Pow(deltaTime, 4);
            double a5 = (6 * rawDistance - 3 * deltaTime * (final_velocity + initial_velocity)) / Math.Pow(deltaTime, 5);

            double position(double t) => initial_velocity * t + a3 * t * t * t + a4 * t * t * t * t + a5 * t * t * t * t * t;
            double positionMinusRadius(double t) => position(t) - (rawDistance - radius);

            double root = Brent.FindRoot(positionMinusRadius, 0, osuCurrObj.StrainTime);

            double timeInCircle = deltaTime - root;
            return 1 / timeInCircle;
        }
    }
}
