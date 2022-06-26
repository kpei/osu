
using System;
using MathNet.Numerics;

namespace osu.Game.Rulesets.Osu.Difficulty
{

    public class MissProbabilityEdgeWorth {

        public double mu;
        public double sigma;
        public double[] coefs;
        private HermiteE hermiteE;

        private class HermiteE {
            private double[] coefs;
            public HermiteE(double[] coefs) {
                this.coefs = coefs;
            }

            public double eval(double x) {
                int nd = coefs.Length;
                double c0 = coefs[coefs.Length - 2];
                double c1 = coefs[coefs.Length - 1];
                for (int i = 3; i <= coefs.Length; i++) {
                    double tmp = (double) c0;
                    nd--;
                    c0 = coefs[coefs.Length - i] - c1 * (nd - 1);
                    c1 = tmp + c1 * x;
                }
                return c0 + c1*x;
            }
        }

        private static class FaaDiBrunoPartition {
            private static readonly (int, int)[][][] partitionCache = new (int, int)[][][] {
                new (int, int)[][] { new (int, int)[] { (1, 1) } },
                new (int, int)[][] { new (int, int)[] { (1, 2) }, new (int, int)[] { (2, 1) } },
                new (int, int)[][] { new (int, int)[] { (1, 3) }, new (int, int)[] { (2, 1), (1, 1) }, new (int, int)[] { (3, 1) } },
                new (int, int)[][] { new (int, int)[] { (1,4) }, 
                    new (int, int)[] { (1, 2), (2, 1) },
                    new (int, int)[] { (2, 2) },
                    new (int, int)[] { (3, 1), (1, 1) },
                    new (int, int)[] { (4, 1) },
                },
            };

            public static (int, int)[][] getPartition(int n) {
                return partitionCache[n];
            }
        };

        private double hermitePdf(double x) => hermiteE.eval(x);
        private double normalPdf(double x) => Math.Exp(-Math.Pow(x, 2) / 2) / Math.Sqrt(2 * Math.PI);

        public MissProbabilityEdgeWorth(double mu, double sigma, double[] coefs) {
            this.mu = mu;
            this.sigma = sigma;
            this.coefs = coefs;
            this.hermiteE = new HermiteE(coefs);
        }
        public MissProbabilityEdgeWorth(double[] hitProbabilities)
        {
            double[] cumulants = calculateAllMoments(hitProbabilities);
            double variance = cumulants[1];
            mu = cumulants[0];
            sigma = Math.Sqrt(variance);
            
            for (int j = 0; j < cumulants.Length; j++) {
                cumulants[j] /= Math.Pow(variance, j);
            }

            coefs = getCoefficientsFromCumulants(sigma, cumulants);
            hermiteE = new HermiteE(coefs);
        }

        public double probabilityOf(double misses) {
            double y = (misses - mu) / sigma;
            return hermitePdf(y) * normalPdf(y) / sigma;
        }

        protected double[] getCoefficientsFromCumulants(double sigma, double[] cumulants) {
            double[] coefs = new double[7] {0,0,0,0,0,0,0};
            coefs[0] = 1;
            for (int s = 0; s < 2; s++) {
                (int, int)[][] partition = FaaDiBrunoPartition.getPartition(s);
                foreach((int, int)[] p in partition) {
                    double term = Math.Pow(sigma, s+1);
                    int r = 0;
                    foreach((int m, int k) in p) {
                        term *= Math.Pow(cumulants[m + 1] / (double) SpecialFunctions.Factorial(m + 2), k) / SpecialFunctions.Factorial(k);
                        r += k;
                    }
                    coefs[s + 1 + 2*r] += term;
                }
            }
            return coefs;
        }
        protected double[] calculateAllMoments(double[] hitProbabilities) {
            double mu = 0;
            double variance = 0;
            double skewness = 0;
            double kurtosis = 0;

            foreach(double p in hitProbabilities) {
                mu += p;
                variance += (1 - p)*p;
                skewness += (1 - 2*p)*(1 - p)*p;
                kurtosis += (1 - 6 * (1 - p) * p)*(1 - p)*p;
            }

            sigma = Math.Sqrt(variance);
            skewness *= 1 / Math.Pow(sigma, 3);
            kurtosis *= 1 / Math.Pow(sigma, 4);

            return new double[4] { mu, variance, skewness, kurtosis };
        }

    }

}