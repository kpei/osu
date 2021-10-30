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
    public class Speed : Skill
    {
        protected override int HistoryLength => 2;

        private const double strain_decay_base = 1 / Math.E;
        private double currentStrain;
        private double maxStrain;

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

        protected override void Process(DifficultyHitObject current)
        {
            currentStrain += strainValueOf(current);
            currentStrain *= Math.Pow(strain_decay_base, current.DeltaTime / 1000);

            if (currentStrain > maxStrain)
            {
                maxStrain = currentStrain;
            }
        }

        public override double DifficultyValue()
        {
            return maxStrain;
        }
    }
}
