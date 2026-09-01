using MathNet.Numerics.Distributions;
using ExcelDataReader;
using NCalc;
using System.Collections.Generic;
using System;
using System.IO;
using MathNet.Numerics.Integration;
using System.Net.NetworkInformation;
using System.Runtime.Intrinsics.Arm;
using System.Reflection.PortableExecutable;
using static System.Runtime.InteropServices.JavaScript.JSType;
using System.IO.Pipelines;
using System.Linq.Expressions;

namespace PipeRoughnessCalculation
{
    public class Measurement
    {
        public double Pin;
        public double Pout;
        public double F;
        public double roughness;
        public bool excludeFromStatistics = false;
    }
    internal class Program
    {
        static double D = 0;
        static double L = 0;
        static double rho = 0;
        static double viscosity = 0;
        static double confidenceLevel = 0;

        static double mean_F = 0;
        static double mean_dP = 0;
        static double mean_roughness = 0;
        static int N = 0;

        static bool ignoreNegativeInput;
        static bool ignoreNonValidRoughness;

        static bool calculateSystematicError = false;
        static double[] arg_errors = new double[6];//D,L,rho,viscosity,P,F

        static List<Measurement> measurements = new List<Measurement>();

        static NCalc.Expression lambdaExpression;
        /*static double err_F;
        static double err_P;
        static double err_D;
        static double err_viscosity;
        static double err_rho;
        static double err_L;*/
        static void Main(string[] args)
        {
            

            Console.WriteLine("Расчёт шероховатости труб");
            ReadInputFile();
            CalculateMeanRoughness();

            double error = CalculateError();

            mean_roughness *= D * 1000.0;
            error *= D * 1000.0;
            string output;
            if (N > 1)
                output = "Эквивалетная шероховатость составляет " + FormatMeasurement(mean_roughness, error) + " мм";
            else
                output = "Не было получено ни одного валидного значения шероховатости";
            Console.WriteLine(output);
            using (StreamWriter sw = new StreamWriter("output.txt"))
                sw.WriteLine(output);
            Console.ReadKey();
        }
        static void CalculateMeanRoughness()
        {
            lambdaExpression.Parameters["D"] = D;
            lambdaExpression.Parameters["L"] = L;
            lambdaExpression.Parameters["rho"] = rho;
            lambdaExpression.Parameters["viscosity"] = viscosity;
            for (int i = 0; i < measurements.Count; i++)
            {
                Console.WriteLine("==================================================");
                Console.WriteLine("Измерение " + (i + 1));
                double Pin = measurements[i].Pin;
                double Pout = measurements[i].Pout;
                Pin *= 101325.0;
                Pout *= 101325.0;
                double F = measurements[i].F;
                if (ignoreNegativeInput && (Pin <= 0 || Pout <= 0 || F <= 0))
                {
                    Console.WriteLine("Измерение с невалидными значениями потока или давления проигнорировано");
                    measurements[i].excludeFromStatistics = true;
                    continue;
                }
                double _roughness = SingleMeasurementRoughnessCalculation(Pin, Pout, F);
                if (ignoreNonValidRoughness && (_roughness < 0 || _roughness > 1))
                {
                    Console.WriteLine("Невалидная относительная шероховатость (" + _roughness + ") проигнорирована");
                    measurements[i].excludeFromStatistics = true;
                    continue;
                }
                measurements[i].roughness = _roughness;
                N++;
                mean_roughness += _roughness;
                mean_F += F;
                mean_dP += (Pin - Pout);
            }
            if (N == 0)
            {
                Console.WriteLine("Не получено ни одного валидного значения шероховатости");
                return;
            }
            mean_roughness /= (double)N;
            mean_F /= (double)N;
            mean_dP /= (double)N;
        }
        static void ReadInputFile()
        {
            System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
            string path = "input.xlsx";
            using (FileStream stream = File.Open(path, FileMode.Open, FileAccess.Read))
            {
                using (var reader = ExcelReaderFactory.CreateReader(stream))
                {
                    bool readingMeasurements = false;
                    while (reader.Read())
                    {
                        Console.WriteLine("Reading..." + reader.GetValue(0));
                        if (readingMeasurements)
                        {
                            Measurement measurement = new Measurement();
                            measurement.Pin = GetNumericInput(reader.GetDouble(1), true);
                            measurement.Pout = GetNumericInput(reader.GetDouble(2), true);
                            measurement.F = GetNumericInput(reader.GetDouble(3), true);
                            measurements.Add(measurement);
                            continue;
                        }
                        switch (reader.GetString(0))
                        {
                            case "D, m":
                                D = GetNumericInput(reader.GetDouble(1), true, 0);
                                break;
                            case "L, m":
                                L = GetNumericInput(reader.GetDouble(1), true, 0);
                                break;
                            case "rho, kg/m3":
                                rho = GetNumericInput(reader.GetDouble(1), true, 0);
                                break;
                            case "viscosity, cP":
                                viscosity = GetNumericInput(reader.GetDouble(1), true, 0);
                                viscosity *= 0.001;
                                break;
                            /*case "N":
                                N_expected = (int)GetNumericInput(reader.GetDouble(1), true, 0);
                                break;*/
                            case "confidence level, %":
                                confidenceLevel= GetNumericInput(reader.GetDouble(1), true, 0,100.0);
                                confidenceLevel *= 0.01;
                                break;
                            case "Ignore negative input":
                                ignoreNegativeInput = GetBooleanInput(reader.GetString(1));
                                break;
                            case "Ignore non-valid roughness":
                                ignoreNonValidRoughness = GetBooleanInput(reader.GetString(1));
                                break;
                            case "Calculate systematic error":
                                calculateSystematicError = GetBooleanInput(reader.GetString(1));
                                break;
                            case "Measurement":
                                readingMeasurements = true;
                                break;
                            case "lambda formula":
                                {
                                    string s = reader.GetString(1);
                                    lambdaExpression = new NCalc.Expression(s);
                                    break;
                                }
                        }
                        if (calculateSystematicError)
                        {
                            switch (reader.GetString(0))
                            {
                                case "err_D, m":
                                    arg_errors[0] = GetNumericInput(reader.GetDouble(1), false, 0);
                                    break;
                                case "err_L, m":
                                    arg_errors[1] = GetNumericInput(reader.GetDouble(1), false, 0);
                                    break;
                                case "err_rho, kg/m3":
                                    arg_errors[2] = GetNumericInput(reader.GetDouble(1), false, 0);
                                    break;
                                case "err_viscosity, sPa":
                                    arg_errors[3] = GetNumericInput(reader.GetDouble(1), false, 0);
                                    arg_errors[3] *= 0.001;
                                    break;
                                case "err_F, kg/s":
                                    arg_errors[4] = GetNumericInput(reader.GetDouble(1), false, 0);
                                    break;
                                case "err_P, atm":
                                    arg_errors[5] = GetNumericInput(reader.GetDouble(1), false, 0);
                                    arg_errors[5] *= 101325.0;
                                    break;
                            }
                        }
                    }
                }
            }
            Console.WriteLine("==================================================");
            Console.WriteLine("==================================================");
            Console.WriteLine("==================================================");
        }
        static string FormatMeasurement(double value, double error)
        {
            if (error <= 0) return value.ToString();

            int errorOrder = (int)Math.Floor(Math.Log10(error));
            int firstDigit = (int)(error / Math.Pow(10, errorOrder));
            if (firstDigit == 1)
                errorOrder -= 1; 

            double roundedError = Math.Round(error, -errorOrder, MidpointRounding.AwayFromZero);
            double roundedValue = Math.Round(value, -errorOrder, MidpointRounding.AwayFromZero);

            string formatSpecifier = errorOrder < 0 ? "F" + (-errorOrder) : "F0";

            return roundedValue.ToString(formatSpecifier) + " +- " + roundedError.ToString(formatSpecifier);
        }
        static bool GetBooleanInput(string s)
        {
            if (s == "д" || s == "Д" || s == "y" || s == "Y" || s == "1" || s == "True" || s == "true")
                return true;
            else if (s == "н" || s == "Н" || s == "n" || s == "N" || s == "0" || s == "False" || s == "false")
                return false;
            else
                throw new Exception();
        }
        // static double GetNumericInput(string prompt, bool strictInequality, double ? min=null,double? max = null)
        static double GetNumericInput(double value, bool strictInequality, double? min = null, double? max = null)
        {
            bool isValid = true;
            if (strictInequality)
            {
                if (min.HasValue && value <= min.Value)
                {
                    Console.WriteLine("Введите число больше " + min.Value);
                    isValid = false;
                }
                if (max.HasValue && value >= max.Value)
                {
                    Console.WriteLine("Введите число меньше " + max.Value);
                    isValid = false;
                }
            }
            else
            {
                if (min.HasValue && value < min.Value)
                {
                    Console.WriteLine("Введите число больше или равное " + min.Value);
                    isValid = false;
                }
                if (max.HasValue && value > max.Value)
                {
                    Console.WriteLine("Введите число меньше или равное " + max.Value);
                    isValid = false;
                }
            }
            if (!isValid)
                throw new Exception();
            return value;
        }
        static void CalculateRandomError(out double epsilon, out double S_x)
        {
            if (N <= 1)
            {
                epsilon = 0;
                S_x = 0;
                return;
            }
            //<RANDOM ERROR>
            double S = 0;
            for (int i = 0; i < measurements.Count; i++)
                if (!measurements[i].excludeFromStatistics)
                    S += (measurements[i].roughness - mean_roughness) * (measurements[i].roughness - mean_roughness);
            S /= (double)(N - 1);
            S = Math.Sqrt(S);
            S_x = S / Math.Sqrt(N);
            double alpha = 1 - confidenceLevel;
            double t = StudentT.InvCDF(0, 1, N - 1, 1.0 - (alpha / 2.0));
            epsilon = t * S_x;
            //</RANDOM ERROR>
        }
        static void CalculateSystematicError(out double theta, out double S_theta )
        {
            double mean_lambda = (Math.PI * Math.PI / 8.0) * (D * D * D * D * D) * mean_dP * rho / (L * mean_F * mean_F);
            mean_lambda /= 0.11;
            double mean_Re = (4.0 / Math.PI) * mean_F / (D * viscosity);
            //<ADDING UP ERRORS>
            double err_sum = 0;
            double lambda_4 = 4.0 * mean_lambda * mean_lambda * mean_lambda * mean_lambda;
            double term = 0;
            /* term = lambda_4 * 2.0 * (err_F / mean_F) + (68.0 / mean_Re) * (err_F / mean_F);
             err_sum += term * term;
             term = lambda_4 * (err_P / mean_dP) * 2.0;
             err_sum += term * term;
             term = lambda_4 * 5.0* (err_D / D)  + (68.0 / mean_Re) * (err_D / D);
             err_sum += term * term;
             term = (68.0 / mean_Re) * (err_viscosity / viscosity);
             err_sum += term * term;
             term = lambda_4 * (err_rho / rho);
             err_sum += term * term;
             term = lambda_4 * (err_L / L);
             err_sum += term * term;*/
            err_sum = Math.Sqrt(err_sum);
            //</ADDING UP ERRORS>
            theta = err_sum;
            S_theta = err_sum / Math.Sqrt(3);
        }
        static double CalculateError()
        {
            double error = 0;
            double epsilon = 0;
            double S_x = 0;
            double theta = 0;
            double S_theta = 0;

            CalculateRandomError(out epsilon, out S_x);
            if (calculateSystematicError)
                CalculateSystematicError(out theta, out S_theta);
            if (S_x + S_theta > 0)
            {
                double K = (epsilon + theta) / (S_x + S_theta);
                double S_sum = Math.Sqrt(S_x * S_x + S_theta * S_theta);
                error = K * S_sum;
            }
            return error;
        }
        static double SingleMeasurementRoughnessCalculation(double Pin, double Pout, double F)
        {
            double dP = Pin - Pout;
            double lambda = (Math.PI * Math.PI / 8.0) * (D * D * D * D * D) * dP * rho / (L * F * F);
            double Re = (4.0 / Math.PI) * F / (D * viscosity);

            /*double Re = (4.0 / Math.PI) * F / (D * viscosity);
            lambda /= 0.11;
            double roughness = lambda * lambda * lambda * lambda - 68.0 / Re;
            return roughness;*/

            lambdaExpression.Parameters["F"] = F;
            lambdaExpression.Parameters["Re"] = Re;
            double roughness = SolveEquationOnLambda(LambdaOnRoughnessFunc, lambda, 0.001);
            return roughness;
        }
        static double LambdaOnRoughnessFunc(double roughness)
        {
            
            lambdaExpression.Parameters["roughness"] = roughness;
            double lambda = 0;

            try
            {
                lambda = Convert.ToDouble(lambdaExpression.Evaluate());
            }
            catch (Exception ex)
            {
                throw new Exception("Impossible to evaluate expression");
            }
            return lambda;
        }
        static double SolveEquationOnLambda(Func<double, double> F,double lambda_measured, double precision, double x_min = 0, double x_max = 1)
        {
            double output;
            if (x_max < x_min)
                throw new Exception("x_max is less than x_min while solving equation");
            double F_min = F(x_min) - lambda_measured;
            double F_max = F(x_max) - lambda_measured;

            if (F_min * F_max > 0)
                throw new Exception("No solution is found");
            if (F_min == 0)
                return x_min;
            if (F_max == 0)
                return F_max;
            double x_middle;
            double F_mid;
            while (x_max-x_min> precision)
            {
                x_middle = 0.5 * (x_max + x_min);
                F_mid = F(x_middle) - lambda_measured;
                if (F_mid == 0)
                    return x_middle;
                if (F_mid * F_max > 0)
                    x_max = x_middle;
                else
                    x_min = x_middle;
                F_min = F(x_min) - lambda_measured;
                F_max = F(x_max) - lambda_measured;
                if (F_max == 0)
                    return x_max;
                if (F_min == 0)
                    return x_min;

            }
            return x_min;
        }
    }
}