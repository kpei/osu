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

        /// <summary>
        /// Builds the parameters that summarizes the miss penalty curve.
        /// The steady skill and difficulty-weighted skill arrays are created from N to 0 misses.
        /// The miss penalty of both curves is then simply each value divided by the final value within the array
        /// which is the approximate Skill to FC value.  <see cref="estimateMissPenaltyParameters"/> is then
        /// used to estimate the final miss penalty attributes.
        /// </summary>
        public static MissPenaltyAttributes buildMissPenaltyAttributes(List<double> difficulties)
        {
            difficulties.Sort();
            int N = difficulties.Count;
            
            // Calculate steady skill and 
            double[] cumulativeSkillPerObject = new double[N];
            double[] steadySkillPerMiss = new double[N];
            for (int i = 0; i < N; i++) {
                double difficulty = difficulties[i];
                double steadySkillOfCurrentMiss = getSteadySkillFromMisses(N, N - i);
                double steadySkillOfLessMiss = getSteadySkillFromMisses(N, N - i - 1);
                double lastSkill = i > 0 ? cumulativeSkillPerObject[i - 1] : 0;

                steadySkillPerMiss[i] = steadySkillOfLessMiss;
                cumulativeSkillPerObject[i] = lastSkill + difficulty * (steadySkillOfLessMiss - steadySkillOfCurrentMiss);
            }

            (double alpha, double beta, double totalError) = estimateMissPenaltyParameters(cumulativeSkillPerObject, steadySkillPerMiss, N);

            return new MissPenaltyAttributes {
                alpha = alpha,
                beta = beta,
                total = totalError,
            };
        }

        /// <summary>
        /// Imagine a map with n objects, where all objects have equal difficulty d.
        /// d * sqrt(2) * s(n,0) will return the FC difficulty of that map.
        /// d * sqrt(2) * s(n,m) will return the m-miss difficulty of that map.
        /// Since we are given FC difficulty, for a score with m misses, we can obtain
        /// the difficulty for m misses by multiplying the difficulty by s(n,m) / s(n,0).
        /// Note that the term d * sqrt(2) gets canceled when taking the ratio.
        /// </summary>
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

        /// <summary>
        /// This method tries to closely approximate <paramref name="cumulativeSkillPerObject"/> miss penalty curve through the use
        /// of the beta distribution parameters and total error.  Since `S(n,m)` is fast/easy to calculate, it calculates the error of each miss
        /// by taking the difference between both miss penalty curves and normalizes them to an empirical pdf via <see cref="calculateEmpiricalPdf"/>.
        /// Note that the difficulty-weighted penalty will always be either equal-to or lower than the steady-skill penalty.
        /// With the empirical pdf available, method of moments for the beta distribution is then utilized to estimate the error pdf.
        /// The alpha, beta of the beta distribution and the sum of all the errors make up the miss penalty attributes.
        /// </summary>
        private static (double alpha, double beta, double totalError) estimateMissPenaltyParameters(double[] cumulativeSkillPerObject, double[] steadySkillPerMiss, int N) {
            List<double> missPenaltyErrors = calculateMissPenaltyErrors(cumulativeSkillPerObject, steadySkillPerMiss);
            List<double> errorPdf = calculateEmpiricalPdf(missPenaltyErrors);
            
            // get its central moments
            double mean = errorPdf.Select((errorPct, index) => errorPct * (N - index) / N).Sum();
            double variance = errorPdf.Select((errorPct, index) => errorPct * Math.Pow(((double) N - index) / N - mean, 2)).Sum();

            // calculate beta distribution parameters.
            double alpha = mean * ((mean * (1 - mean) / variance) - 1);
            double beta = alpha * (1 - mean) / mean;

            return (alpha, beta, missPenaltyErrors.Sum());
        }

        /// <summary>
        /// Calculates the raw difference between the desired miss penalty against the steady-skill miss penalty.
        /// The error array is what is used to estimate the miss penalty error curve.
        /// </summary>
        private static List<double> calculateMissPenaltyErrors(double[] cumulativeSkillPerObject, double[] steadySkillPerMiss) {
            int N = cumulativeSkillPerObject.Count();
            double steadySkillforFC = steadySkillPerMiss[N-1];
            double cumulativeSkillforFC = cumulativeSkillPerObject[N-1];

            IEnumerable<double> missPenaltyErrors = cumulativeSkillPerObject.Zip(steadySkillPerMiss, (cumSkill, steadySkill) => cumSkill / cumulativeSkillforFC - steadySkill / steadySkillforFC);

            return missPenaltyErrors.Take(N-1).ToList();
        }

        /// <summary>
        /// Converts a list of values into an empirical probability density function by normalizing
        /// it's values against the total.  This function assumes <paramref name="values"/> is already sorted
        /// ascendingly.
        /// </summary>
        private static List<double> calculateEmpiricalPdf(List<double> values) {
            double total = values.Sum();
            return values.Select(v => v / total).ToList();
        }

    }
}
