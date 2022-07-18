// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

#nullable disable

using System;
using System.Collections.Generic;
using System.Linq;
using MathNet.Numerics;
using MathNet.Numerics.Distributions;
using osu.Game.Rulesets.Difficulty;
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
        private double missPenalty;

        private double effectiveMissCount;

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
                MissPenalty = missPenalty,
                Total = totalValue
            };
        }

        private double computeAimValue(ScoreInfo score, OsuDifficultyAttributes attributes)
        {
            double aimDifficulty = attributes.AimDifficulty;

            if (totalSuccessfulHits == 0)
                return 0;

            // Penalize misses based on the average of two methods: equal hit probability approximation & log-normal order statistics
            if (effectiveMissCount > 0)
            {
                // This is an approximation of skill level derived from assuming all objects have equal hit probabilities.
                double baseAimPenalty = calculateBaseMissPenalty();
                double? spreadBasedAimPenalty = calculateSpreadAdjustedMissPenalty(attributes.AimDifficultySpread);

                missPenalty = baseAimPenalty;
                if (spreadBasedAimPenalty is not null) {
                    missPenalty *= 0.5;
                    missPenalty += 0.5 * (double) spreadBasedAimPenalty;
                }

                // Since star rating is difficulty^0.829842642, we should raise the miss penalty to this power as well.
                aimDifficulty *= Math.Pow(missPenalty, 0.829842642);
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

        /// <summary>
        /// Applies <see cref="calculateSkillFromMisses"/> to get a basic miss penalty
        /// Since we are given FC difficulty, for a score with m misses, we can obtain
        /// the difficulty for m misses by multiplying the difficulty by s(n,m) / s(n,0).
        /// </summary>
        private double calculateBaseMissPenalty()
        {
            int n = totalHits;

            if (n == 0)
                return 0;

            return calculateSkillFromMisses(n, effectiveMissCount) / calculateSkillFromMisses(n, 0);
        }

        private double getExpectedDifficultyOfOrder(int order, int totalHits, double difficultySpread)
        {
            double alpha = 0.375;
            double v = (order - alpha) / (totalHits - 2*alpha + 1);
            return Math.Exp(difficultySpread * Normal.InvCDF(0, 1, v));
        }

        private double? calculateSpreadAdjustedMissPenalty(double? aimDifficultySpread)
        {
            if (aimDifficultySpread is null)
                return null;

            if (totalHits == 0)
                return 0;
            
            double estimateSkillWithSpread(int from, int to, int totalHits, double difficultySpread, double precision = 1e-3)
            {
                double skill = 0;
                for (int misses = from; misses < to; misses++) {
                    double incrementalSkill = calculateSkillFromMisses(totalHits, misses) - calculateSkillFromMisses(totalHits, misses + 1);
                    double skillFromHit = getExpectedDifficultyOfOrder(totalHits - misses, totalHits, difficultySpread) * incrementalSkill;

                    skill += skillFromHit;
                    if (skillFromHit < precision) break;
                }

                return skill;
            }

            double skillToFc = estimateSkillWithSpread(0, totalHits - 1, totalHits, (double) aimDifficultySpread);
            double skillToMiss = estimateSkillWithSpread(0, (int) Math.Round(effectiveMissCount), totalHits, (double) aimDifficultySpread);

            return 1 - skillToMiss / skillToFc;
        }

        /// <summary>
        /// Imagine a map with <paramref name="totalHits"/> number of objects, where all hits have equal difficulty d.
        /// This function [S(n,m)] calculates the total skill required to achieve at most <paramref name="misses"/>.
        /// For example: S(n, 0) calculates FC difficulty
        /// </summary>
        private double calculateSkillFromMisses(double totalHits, double misses)
        {
            double y = SpecialFunctions.ErfInv((totalHits - misses) / (totalHits + 1));
            // Derivatives of ErfInv:
            double y1 = Math.Exp(y * y) * Math.Sqrt(Math.PI) / 2;
            double y2 = 2 * y * y1 * y1;
            double y3 = 2 * y1 * (y * y2 + (2 * (y * y) + 1) * (y1 * y1));
            double y4 = 2 * y1 * (y * y3 + (6 * (y * y) + 3) * y1 * y2 + (4 * (y * y * y) + 6 * y) * (y1 * y1 * y1));
            // Central moments of Beta distribution:
            double a = totalHits - misses;
            double b = misses + 1;
            double u2 = a * b / ((a + b) * (a + b) * (a + b + 1));
            double u3 = 2 * (b - a) * a * b / ((a + b + 2) * (a + b) * (a + b) * (a + b) * (a + b + 1));
            double u4 = (3 + 6 * ((a - b) * (a + b + 1) - a * b * (a + b + 2)) / (a * b * (a + b + 2) * (a + b + 3))) * (u2 * u2);
            return Math.Sqrt(2) * (y + 0.5 * y2 * u2 + 1 / 6.0 * y3 * u3 + 1 / 24.0 * y4 * u4);
        }

        private double getComboScalingFactor(OsuDifficultyAttributes attributes) => attributes.MaxCombo <= 0 ? 1.0 : Math.Min(Math.Pow(scoreMaxCombo, 0.8) / Math.Pow(attributes.MaxCombo, 0.8), 1.0);
        private int totalHits => countGreat + countOk + countMeh + countMiss;
        private int totalSuccessfulHits => countGreat + countOk + countMeh;
    }
}
