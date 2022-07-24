// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

#nullable disable

using System;
using System.Collections.Generic;
using System.Linq;
using MathNet.Numerics;
using MathNet.Numerics.Distributions;
using osu.Game.Rulesets.Difficulty;
using osu.Game.Rulesets.Osu.Difficulty.Helpers;
using osu.Game.Rulesets.Osu.Mods;
using osu.Game.Rulesets.Scoring;
using osu.Game.Scoring;

namespace osu.Game.Rulesets.Osu.Difficulty
{
    public class OsuPerformanceCalculator : PerformanceCalculator
    {
        private double accuracy;
        private int scoreMaxCombo;
        private int countGreat;
        private int countOk;
        private int countMeh;
        private int countMiss;

        private double effectiveMissCount;

        // @TODO: remove after balancing
        private double aimMissScaling = 1;
        private double vanillaAimMissScaling = 1;

        public OsuPerformanceCalculator()
            : base(new OsuRuleset())
        {
        }

        protected override PerformanceAttributes CreatePerformanceAttributes(ScoreInfo score, DifficultyAttributes attributes)
        {
            var osuAttributes = (OsuDifficultyAttributes)attributes;

            accuracy = score.Accuracy;
            scoreMaxCombo = score.MaxCombo;
            countGreat = score.Statistics.GetValueOrDefault(HitResult.Great);
            countOk = score.Statistics.GetValueOrDefault(HitResult.Ok);
            countMeh = score.Statistics.GetValueOrDefault(HitResult.Meh);
            countMiss = score.Statistics.GetValueOrDefault(HitResult.Miss);
            effectiveMissCount = calculateEffectiveMissCount(osuAttributes);

            const double multiplier = 1.0; // This is being adjusted to keep the final pp value scaled around what it used to be when changing things.

            double aimValue = computeAimValue(score, osuAttributes);
            double speedValue = computeSpeedValue(score, osuAttributes);
            double accuracyValue = computeAccuracyValue(score, osuAttributes);
            double flashlightValue = computeFlashlightValue(score, osuAttributes);
            double totalValue = multiplier * (aimValue + speedValue + accuracyValue + flashlightValue);

            return new OsuPerformanceAttributes
            {
                Aim = aimValue,
                Speed = speedValue,
                Accuracy = accuracyValue,
                Flashlight = flashlightValue,
                EffectiveMissCount = effectiveMissCount,
                AimMissScaling = aimMissScaling,
                VanillaAimMissScaling = vanillaAimMissScaling,
                Total = totalValue
            };
        }

        private double computeAimValue(ScoreInfo score, OsuDifficultyAttributes attributes)
        {
            double aimDifficulty = attributes.AimDifficulty;

            if (totalSuccessfulHits == 0)
                return 0;

            // Penalize misses. This is an approximation of skill level derived from assuming all objects have equal hit probabilities.
            if (effectiveMissCount > 0)
            {
                // Since star rating is difficulty^0.829842642, we should raise the miss penalty to this power as well.
                aimMissScaling = Math.Pow(calculateMissPenalty(attributes.MissPenaltyAttributes), 0.829842642);
                vanillaAimMissScaling = 0.97 * Math.Pow(1 - Math.Pow(effectiveMissCount / totalHits, 0.775), effectiveMissCount);
                aimDifficulty *= aimMissScaling;
            }

            double aimValue = Math.Pow(aimDifficulty, 3);

            // Temporarily handling of slider-only maps:
            if (attributes.HitCircleCount - countMiss == 0)
                return aimValue;

            double? deviation = calculateDeviation(attributes);

            switch (deviation)
            {
                case null:
                    return aimValue;

                case double.PositiveInfinity:
                    return 0;
            }

            if (score.Mods.Any(h => h is OsuModHidden))
            {
                // We want to give more reward for lower AR when it comes to aim and HD. This nerfs high AR and buffs lower AR.
                aimValue *= 1.0 + 0.04 * (12.0 - attributes.ApproachRate);
            }

            double deviationScaling = SpecialFunctions.Erf(50 / (Math.Sqrt(2) * (double)deviation));
            aimValue *= deviationScaling;

            return aimValue;
        }

        private double computeSpeedValue(ScoreInfo score, OsuDifficultyAttributes attributes)
        {
            if (score.Mods.Any(h => h is OsuModRelax))
                return 0.0;

            double speedValue = Math.Pow(attributes.SpeedDifficulty, 3);

            if (totalSuccessfulHits == 0)
                return 0;

            double? deviation = calculateDeviation(attributes);

            switch (deviation)
            {
                case null:
                    return speedValue;

                case double.PositiveInfinity:
                    return 0;
            }

            if (score.Mods.Any(m => m is OsuModHidden))
            {
                // We want to give more reward for lower AR when it comes to aim and HD. This nerfs high AR and buffs lower AR.
                speedValue *= 1.0 + 0.04 * (12.0 - attributes.ApproachRate);
            }

            double deviationScaling = SpecialFunctions.Erf(20 / (Math.Sqrt(2) * (double)deviation));
            speedValue *= deviationScaling;

            return speedValue;
        }

        private double computeAccuracyValue(ScoreInfo score, OsuDifficultyAttributes attributes)
        {
            if (score.Mods.Any(h => h is OsuModRelax))
                return 0.0;

            if (attributes.HitCircleCount == 0 || totalSuccessfulHits == 0)
                return 0;

            double? deviation = calculateDeviation(attributes);

            if (deviation == null)
            {
                return 0;
            }

            double accuracyValue = 90 * Math.Pow(7.5 / (double)deviation, 2);

            if (score.Mods.Any(m => m is OsuModHidden))
                accuracyValue *= 1.08;

            return accuracyValue;
        }

        private double computeFlashlightValue(ScoreInfo score, OsuDifficultyAttributes attributes)
        {
            if (!score.Mods.Any(h => h is OsuModFlashlight))
                return 0.0;

            double rawFlashlight = attributes.FlashlightDifficulty;

            if (score.Mods.Any(m => m is OsuModTouchDevice))
                rawFlashlight = Math.Pow(rawFlashlight, 0.8);

            double flashlightValue = Math.Pow(rawFlashlight, 2.0) * 25.0;

            // Penalize misses by assessing # of misses relative to the total # of objects. Default a 3% reduction for any # of misses.
            if (effectiveMissCount > 0)
                flashlightValue *= 0.97 * Math.Pow(1 - Math.Pow(effectiveMissCount / totalHits, 0.775), Math.Pow(effectiveMissCount, .875));

            flashlightValue *= getComboScalingFactor(attributes);

            // Account for shorter maps having a higher ratio of 0 combo/100 combo flashlight radius.
            flashlightValue *= 0.7 + 0.1 * Math.Min(1.0, totalHits / 200.0) +
                               (totalHits > 200 ? 0.2 * Math.Min(1.0, (totalHits - 200) / 200.0) : 0.0);

            // Scale the flashlight value with accuracy _slightly_.
            flashlightValue *= 0.5 + accuracy / 2.0;
            // It is important to also consider accuracy difficulty when doing that.
            flashlightValue *= 0.98 + Math.Pow(attributes.OverallDifficulty, 2) / 2500;

            return flashlightValue;
        }

        private double? calculateDeviation(OsuDifficultyAttributes attributes)
        {
            if (attributes.HitCircleCount == 0)
                return null;

            double greatHitWindow = 80 - 6 * attributes.OverallDifficulty;
            double greatProbability = (attributes.HitCircleCount - countOk - countMeh - countMiss) / (attributes.HitCircleCount + 1.0);

            if (greatProbability <= 0)
            {
                return double.PositiveInfinity;
            }

            double deviation = greatHitWindow / (Math.Sqrt(2) * SpecialFunctions.ErfInv(greatProbability));

            return deviation;
        }

        private double calculateEffectiveMissCount(OsuDifficultyAttributes attributes)
        {
            // Guess the number of misses + slider breaks from combo
            double comboBasedMissCount = 0.0;

            if (attributes.SliderCount > 0)
            {
                double fullComboThreshold = attributes.MaxCombo - 0.1 * attributes.SliderCount;
                if (scoreMaxCombo < fullComboThreshold)
                    comboBasedMissCount = fullComboThreshold / Math.Max(1.0, scoreMaxCombo);
            }

            // Clamp miss count since it's derived from combo and can be higher than total hits and that breaks some calculations
            comboBasedMissCount = Math.Min(comboBasedMissCount, totalHits);

            return Math.Max(countMiss, comboBasedMissCount);
        }

        private double calculateMissPenalty(MissPenaltyAttributes attributes)
        {
            if (totalHits == 0) return 0;

            double missScaling = 0.97 * Math.Pow(1 - Math.Pow(effectiveMissCount / totalHits, 0.775), effectiveMissCount);

            (double start, double end) convertMissCountToHitRange(int n, double effectiveMissCount) {
                double miss = effectiveMissCount - 1;
                double start = Math.Max(0, miss - 0.5);
                double end = Math.Min(n - 1, miss + 0.5); 
                return (start: start / n, end: end / n);
            }

            (double start, double end) hitRange = convertMissCountToHitRange(totalHits, effectiveMissCount);
            Beta errorDist = new Beta(attributes.alpha, attributes.beta);
            double difficultyErrorPct = errorDist.CumulativeDistribution(hitRange.end) - errorDist.CumulativeDistribution(hitRange.start);
            
            return missScaling + attributes.total * difficultyErrorPct;
        }

        private double getComboScalingFactor(OsuDifficultyAttributes attributes) => attributes.MaxCombo <= 0 ? 1.0 : Math.Min(Math.Pow(scoreMaxCombo, 0.8) / Math.Pow(attributes.MaxCombo, 0.8), 1.0);
        private int totalHits => countGreat + countOk + countMeh + countMiss;
        private int totalSuccessfulHits => countGreat + countOk + countMeh;
    }
}
