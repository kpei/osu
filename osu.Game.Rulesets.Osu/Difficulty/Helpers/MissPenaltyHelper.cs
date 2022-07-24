// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

#nullable disable

using System;
using System.Collections.Generic;
using MathNet.Numerics;
using System.Linq;

namespace osu.Game.Rulesets.Osu.Difficulty.Helpers
{

    public struct MissPenaltyAttributes {
        public double alpha;
        public double beta;
        public double total;
    }
    public static class MissPenaltyHelper
    {

        public static double getSteadySkillFromMisses(int totalDifficulties, double totalMisses) {
            if (totalDifficulties == totalMisses) return 0;

            double y = SpecialFunctions.ErfInv((totalDifficulties - totalMisses) / (totalDifficulties + 1));
            
            // Derivatives of ErfInv:
            double y1 = Math.Exp(y * y) * Math.Sqrt(Math.PI) / 2;
            double y2 = 2 * y * y1 * y1;
            double y3 = 2 * y1 * (y * y2 + (2 * (y * y) + 1) * (y1 * y1));
            double y4 = 2 * y1 * (y * y3 + (6 * (y * y) + 3) * y1 * y2 + (4 * (y * y * y) + 6 * y) * (y1 * y1 * y1));
            
            // Central moments of Beta distribution:
            double a = totalDifficulties - totalMisses;
            double b = totalMisses + 1;
            double u2 = a * b / ((a + b) * (a + b) * (a + b + 1));
            double u3 = 2 * (b - a) * a * b / ((a + b + 2) * (a + b) * (a + b) * (a + b) * (a + b + 1));
            double u4 = (3 + 6 * ((a - b) * (a + b + 1) - a * b * (a + b + 2)) / (a * b * (a + b + 2) * (a + b + 3))) * (u2 * u2);
            
            return Math.Sqrt(2) * (y + 0.5 * y2 * u2 + 1 / 6.0 * y3 * u3 + 1 / 24.0 * y4 * u4);
        }

        private static List<double> calculateMissPenaltyErrors(double[] cumulativeSkillPerObject, double[] steadySkillPerMiss) {
            int N = cumulativeSkillPerObject.Count();
            double steadySkillforFC = steadySkillPerMiss[N-1];
            double cumulativeSkillforFC = cumulativeSkillPerObject[N-1];

            IEnumerable<double> missPenaltyErrors = cumulativeSkillPerObject.Zip(steadySkillPerMiss, (cumSkill, steadySkill) => cumSkill / cumulativeSkillforFC - steadySkill / steadySkillforFC);

            return missPenaltyErrors.Take(N-1).ToList();
        }

        private static List<double> calculateEmpiricalPdf(List<double> values) {
            double total = values.Sum();
            return values.Select(v => v / total).ToList();
        }

        public static MissPenaltyAttributes buildMissPenaltyAttributes(List<double> difficulties)
        {
            difficulties.Sort();
            int N = difficulties.Count;
            
            double[] cumulativeSkillPerObject = new double[N];
            double[] steadySkillPerMiss = new double[N];
            for (int misses = 0; misses < N; misses++) {
                double difficulty = difficulties[misses];
                double steadySkillOfCurrentMiss = getSteadySkillFromMisses(N, N - misses);
                double steadySkillOfLessMiss = getSteadySkillFromMisses(N, N - misses - 1);
                double lastSkill = misses > 0 ? cumulativeSkillPerObject[misses - 1] : 0;

                steadySkillPerMiss[misses] = steadySkillOfLessMiss;
                cumulativeSkillPerObject[misses] = lastSkill + difficulty * (steadySkillOfLessMiss - steadySkillOfCurrentMiss);
            }

            List<double> missPenaltyErrors = calculateMissPenaltyErrors(cumulativeSkillPerObject, steadySkillPerMiss);
            List<double> errorPdf = calculateEmpiricalPdf(missPenaltyErrors);
            
            double mean = errorPdf.Select((errorPct, index) => errorPct * (N - index) / N).Sum();
            double variance = errorPdf.Select((errorPct, index) => errorPct * Math.Pow(((double) N - index) / N - mean, 2)).Sum();

            double alpha = mean * ((mean * (1 - mean) / variance) - 1);
            double beta = alpha * (1 - mean) / mean;

            return new MissPenaltyAttributes {
                alpha = alpha,
                beta = beta,
                total = missPenaltyErrors.Sum(),
            };
        }

    }
}
