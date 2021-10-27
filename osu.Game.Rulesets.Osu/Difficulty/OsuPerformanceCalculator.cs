// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using MathNet.Numerics;
using osu.Game.Rulesets.Difficulty;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Osu.Mods;
using osu.Game.Rulesets.Scoring;
using osu.Game.Scoring;

namespace osu.Game.Rulesets.Osu.Difficulty
{
    public class OsuPerformanceCalculator : PerformanceCalculator
    {
        public new OsuDifficultyAttributes Attributes => (OsuDifficultyAttributes)base.Attributes;

        private Mod[] mods;

        private double accuracy;
        private int scoreMaxCombo;
        private int countGreat;
        private int countOk;
        private int countMeh;
        private int countMiss;

        private double effectiveMissCount;

        public OsuPerformanceCalculator(Ruleset ruleset, DifficultyAttributes attributes, ScoreInfo score)
            : base(ruleset, attributes, score)
        {
        }

        public override double Calculate(Dictionary<string, double> categoryRatings = null)
        {
            mods = Score.Mods;
            accuracy = Score.Accuracy;
            scoreMaxCombo = Score.MaxCombo;
            countGreat = Score.Statistics.GetValueOrDefault(HitResult.Great);
            countOk = Score.Statistics.GetValueOrDefault(HitResult.Ok);
            countMeh = Score.Statistics.GetValueOrDefault(HitResult.Meh);
            countMiss = Score.Statistics.GetValueOrDefault(HitResult.Miss);
            effectiveMissCount = calculateEffectiveMissCount();

            double multiplier = 1;

            // Custom multipliers for NoFail and SpunOut.
            if (mods.Any(m => m is OsuModNoFail))
                multiplier *= Math.Max(0.90, 1.0 - 0.02 * effectiveMissCount);

            if (mods.Any(m => m is OsuModSpunOut))
                multiplier *= 1.0 - Math.Pow((double)Attributes.SpinnerCount / totalHits, 0.85);

            if (mods.Any(h => h is OsuModRelax))
            {
                effectiveMissCount += countOk + countMeh;
                multiplier *= 0.6;
            }

            double aimValue = computeAimValue();
            double speedValue = computeSpeedValue();
            double accuracyValue = computeAccuracyValue();
            double flashlightValue = computeFlashlightValue();
            double totalValue = multiplier * (aimValue + speedValue + accuracyValue + flashlightValue);

            if (categoryRatings != null)
            {
                categoryRatings.Add("Aim", aimValue);
                categoryRatings.Add("Speed", speedValue);
                categoryRatings.Add("Accuracy", accuracyValue);
                categoryRatings.Add("Flashlight", flashlightValue);
                categoryRatings.Add("OD", Attributes.OverallDifficulty);
                categoryRatings.Add("AR", Attributes.ApproachRate);
                categoryRatings.Add("Max Combo", Attributes.MaxCombo);
            }

            return totalValue;
        }

        private double computeAimValue()
        {
            double aimDifficulty = Attributes.AimStrain;

            if (mods.Any(m => m is OsuModTouchDevice))
                aimDifficulty = Math.Pow(aimDifficulty, 0.8);

            // Penalize misses. This is an approximation of skill level derived from assuming all objects have equal hit probabilities.
            aimDifficulty *= Math.Pow(SpecialFunctions.ErfInv(1 - 1.0 / totalHits - effectiveMissCount / totalHits) / SpecialFunctions.ErfInv(1 - 1.0 / totalHits), 0.829842642);

            double aimValue = Math.Pow(aimDifficulty, 3);

            if (mods.Any(m => m is OsuModBlinds))
            {
                aimValue *= 1.3 + totalHits * (0.0016 / (1 + 2 * effectiveMissCount)) * Math.Pow(accuracy, 16) * (1 - 0.003 * Attributes.DrainRate * Attributes.DrainRate);
            }
            else if (mods.Any(h => h is OsuModHidden))
            {
                // We want to give more reward for lower AR when it comes to aim and HD. This nerfs high AR and buffs lower AR.
                aimValue *= 1.0 + 0.04 * (12.0 - Attributes.ApproachRate);
            }

            return aimValue;
        }

        private double computeSpeedValue()
        {
            double speedValue = Math.Pow(Attributes.SpeedStrain, 3);
            double? deviation = getDeviation();

            if (deviation == null)
                return 0;

            double deviationScaling = SpecialFunctions.Erf(13 / (Math.Sqrt(2) * (double)deviation));
            speedValue *= deviationScaling;

            return speedValue;
        }

        private double computeAccuracyValue()
        {
            if (Attributes.HitCircleCount == 0)
                return 0;

            double? deviation = getDeviation();

            if (deviation == null)
            {
                return 0;
            }

            double accuracyValue = 4750 / Math.Pow((double)deviation, 2);

            if (mods.Any(m => m is OsuModHidden))
                accuracyValue *= 1.08;
            if (mods.Any(m => m is OsuModFlashlight))
                accuracyValue *= 1.02;

            return accuracyValue;
        }

        private double? getDeviation()
        {
            if (Attributes.HitCircleCount == 0)
                return null;

            double modifiedAccuracy = 1 - (double)(2 * countMeh + countOk + 1) / (Attributes.HitCircleCount - countMiss + 2);
            if (modifiedAccuracy < 0)
                return null;

            double deviation = (79.5 - 6 * Attributes.OverallDifficulty) / (Math.Sqrt(2) * SpecialFunctions.ErfInv(modifiedAccuracy));
            return deviation;
        }

        private double computeFlashlightValue()
        {
            if (!mods.Any(h => h is OsuModFlashlight))
                return 0.0;

            double rawFlashlight = Attributes.FlashlightRating;

            if (mods.Any(m => m is OsuModTouchDevice))
                rawFlashlight = Math.Pow(rawFlashlight, 0.8);

            double flashlightValue = Math.Pow(rawFlashlight, 2.0) * 25.0;

            // Add an additional bonus for HDFL.
            if (mods.Any(h => h is OsuModHidden))
                flashlightValue *= 1.3;

            // Penalize misses by assessing # of misses relative to the total # of objects. Default a 3% reduction for any # of misses.
            if (effectiveMissCount > 0)
                flashlightValue *= 0.97 * Math.Pow(1 - Math.Pow(effectiveMissCount / totalHits, 0.775), Math.Pow(effectiveMissCount, .875));

            // Combo scaling.
            if (Attributes.MaxCombo > 0)
                flashlightValue *= Math.Min(Math.Pow(scoreMaxCombo, 0.8) / Math.Pow(Attributes.MaxCombo, 0.8), 1.0);

            // Account for shorter maps having a higher ratio of 0 combo/100 combo flashlight radius.
            flashlightValue *= 0.7 + 0.1 * Math.Min(1.0, totalHits / 200.0) +
                               (totalHits > 200 ? 0.2 * Math.Min(1.0, (totalHits - 200) / 200.0) : 0.0);

            // Scale the flashlight value with accuracy _slightly_.
            flashlightValue *= 0.5 + accuracy / 2.0;
            // It is important to also consider accuracy difficulty when doing that.
            flashlightValue *= 0.98 + Math.Pow(Attributes.OverallDifficulty, 2) / 2500;

            return flashlightValue;
        }

        private double calculateEffectiveMissCount()
        {
            // guess the number of misses + slider breaks from combo
            double comboBasedMissCount = 0.0;

            if (Attributes.SliderCount > 0)
            {
                double fullComboThreshold = Attributes.MaxCombo - 0.1 * Attributes.SliderCount;
                if (scoreMaxCombo < fullComboThreshold)
                    comboBasedMissCount = fullComboThreshold / Math.Max(1.0, scoreMaxCombo);
            }

            // we're clamping misscount because since its derived from combo it can be higher than total hits and that breaks some calculations
            comboBasedMissCount = Math.Min(comboBasedMissCount, totalHits);

            return Math.Max(countMiss, comboBasedMissCount);
        }

        private int totalHits => countGreat + countOk + countMeh + countMiss;
        private int totalSuccessfulHits => countGreat + countOk + countMeh;
    }
}
