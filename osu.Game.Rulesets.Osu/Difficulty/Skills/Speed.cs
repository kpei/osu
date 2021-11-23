// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using osu.Game.Rulesets.Difficulty.Preprocessing;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Osu.Difficulty.Preprocessing;
using osu.Game.Rulesets.Difficulty.Skills;
using osu.Game.Rulesets.Osu.Objects;

namespace osu.Game.Rulesets.Osu.Difficulty.Skills
{
    /// <summary>
    /// Represents the skill required to press keys with regards to keeping up with the speed at which objects need to be hit.
    /// </summary>
    public class Speed : StrainSkill
    {
        private const double strain_decay_base = 0.3;
        private double currentStrain;

        public Speed(Mod[] mods)
            : base(mods)
        {
        }

        private double strainValueOf(DifficultyHitObject current)
        {
            var currentObject = (OsuDifficultyHitObject)current;
            if (currentObject.BaseObject is Spinner)
                return 0;

            return 1 / currentObject.DeltaTime;
        }

        private double strainDecay(double ms) => Math.Pow(strain_decay_base, ms / 1000);
        protected override double CalculateInitialStrain(double time) => currentStrain * strainDecay(time - Previous[0].StartTime);

        protected override double StrainValueAt(DifficultyHitObject current)
        {
            currentStrain *= strainDecay(current.DeltaTime);
            currentStrain += strainValueOf(current);
            return currentStrain;
        }
    }
}
