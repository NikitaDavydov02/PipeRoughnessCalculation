using MathNet.Numerics.Distributions;
using MathNet.Numerics.Integration;
using System.Net.NetworkInformation;
using System.Runtime.Intrinsics.Arm;

namespace PipeRoughnessCalculation
{
    internal class Program
    {
        static double D = 0;
        static double L = 0;
        static double rho = 0;
        static double viscosity = 0;
        static double confidenceLevel = 0;
        static double mean_F = 0;
        static double mean_dP = 0;
        static void Main(string[] args)
        {
            Console.WriteLine("Расчёт шероховатости труб");

            D = GetNumericInput("Введите диаметр трубы (м)",true,0);
            L = GetNumericInput("Введите длину трубы (м)", true, 0);
            rho = GetNumericInput("Введите плотность вещества (кг/м3)", true, 0);
            viscosity = GetNumericInput("Введите вязкость вещества (сПз)", true,0);
            viscosity *= 0.001;
            /*Pin = GetNumericInput("Введите входное давление (атм)", true,0);
            Pin_atm = Pin;
            Pin *= 101325.0;*/

            int N_expected = 0;
            while (N_expected <= 0)
            {
                Console.WriteLine("Введите число измерений:");
                N_expected = Convert.ToInt32(Console.ReadLine());
            }
            if (N_expected > 1)
            {
                confidenceLevel = GetNumericInput("Введите уровень значимости (%) для расчёта случайной погрешности", true, 0,100.0);
                confidenceLevel *= 0.01;
            }
            //********************************************************************


            bool ignoreNegativeInput = GetBooleanInput("Игнорировать неположительные значения давления и потока?");
            bool ignoreNonValidRoughness = GetBooleanInput("Игнорировать нефизичные значения шероховатости? ");
           
            double[] roughness = new double[N_expected];
            double mean_roughness = 0;
            int N = 0;
                
            for(int i=0; i < N_expected; i++)
            {
                Console.WriteLine("==================================================");
                Console.WriteLine("Измерение "+(i+1));
                double Pin = GetNumericInput("Введите входное давление (атм)", true);
                double Pout = GetNumericInput("Введите выходное давление (атм)", true);
                Pin *= 101325.0;
                Pout *= 101325.0;
                double F = GetNumericInput("Введите поток через трубу (кг/с)", true);
                if (ignoreNegativeInput && (Pin <= 0  || Pout <= 0 || F <= 0))
                {
                    Console.WriteLine("Измерение с невалидными значениями потока или давления проигнорировано");
                    continue;
                }
                double _roughness = SingleMeasurementRoughnessCalculation(Pin, Pout, F);
                if (ignoreNonValidRoughness && (_roughness < 0 || _roughness > 1))
                {
                    Console.WriteLine("Невалидная относительная шероховатость ("+ _roughness+ ") проигнорирована");
                    continue;
                }
                roughness[N] = _roughness;
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
            mean_dP/= (double)N;
            //*******************************************************************************
            //<ERROR CALCULATION>
            double error = 0;
            double epsilon = 0;
            double S_x = 0;
            double theta = 0;
            double S_theta = 0;
            if(N>1)
            {
                //<RANDOM ERROR>
                double S = 0;
                for (int i = 0; i < N; i++)
                    S += (roughness[i] - mean_roughness)*(roughness[i] - mean_roughness);
                S /= (double)(N - 1);
                S = Math.Sqrt(S);
                S_x = S / Math.Sqrt(N);
                double alpha = 1 - confidenceLevel;
                double t = StudentT.InvCDF(0, 1, N-1, 1.0 - (alpha / 2.0));
                epsilon = t * S_x;
                //</RANDOM ERROR>
            }
            //<SYSTEMATIC ERROR>
            bool calculateSystematicError = GetBooleanInput("Рассчитывать систематическую погрешность?");
            if (calculateSystematicError)
            {
                double err_F = GetNumericInput("Введите погрешность измерения потока (кг/с)",false,0);
                double err_P = 101325.0*GetNumericInput("Введите погрешность измерения давления (атм)", false, 0);
                double err_D = GetNumericInput("Введите погрешность диаметра (м)", false, 0);
                double err_viscosity = 0.001 * GetNumericInput("Введите погрешность вязкости (сПз)", false, 0);
                double err_rho = GetNumericInput("Введите погрешность плотности (кг/м3)", false, 0);
                double err_L = GetNumericInput("Введите погрешность длины трубы (м)", false, 0);
                //***
                double mean_lambda = (Math.PI * Math.PI / 8.0) * (D * D * D * D * D) * mean_dP * rho / (L * mean_F * mean_F);
                mean_lambda /= 0.11;
                double mean_Re = (4.0 / Math.PI) * mean_F / (D * viscosity);
                //<ADDING UP ERRORS>
                double err_sum = 0;
                double lambda_4 = 4.0 * mean_lambda * mean_lambda * mean_lambda * mean_lambda;
                double term = 0;
                term = lambda_4 * 2.0 * (err_F / mean_F) + (68.0 / mean_Re) * (err_F / mean_F);
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
                err_sum += term * term;
                err_sum = Math.Sqrt(err_sum);
                //</ADDING UP ERRORS>
                theta = err_sum;
                S_theta = err_sum / Math.Sqrt(3);

            }
            //</SYSTEMATIC ERROR>
            if(S_x + S_theta > 0)
            {
                double K = (epsilon + theta) / (S_x + S_theta);
                double S_sum = Math.Sqrt(S_x*S_x+S_theta*S_theta);
                error = K * S_sum;
            }
            //</ERROR CALCULATION>
            mean_roughness *= D * 1000.0;
            error *= D * 1000.0;
            string output = "Эквивалетная шероховатость составляет " + FormatMeasurement(mean_roughness, error) + " мм";
            Console.WriteLine(output);
            Console.ReadKey();
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
        static bool GetBooleanInput(string prompt)
        {
            while (true)
            {
                Console.WriteLine(prompt + " (д/н)(y/n)(1/0)?");
                string s = Console.ReadLine();
                if (s == "д" || s == "Д" || s == "y" || s == "Y" || s == "1")
                    return true;
                if (s == "н" || s == "Н" || s == "n" || s == "N" || s == "0")
                    return false;
            }
        }
        static double GetNumericInput(string prompt, bool strictInequality, double ? min=null,double? max = null)
        {
            double value;
            bool isValid;
            do
            {
                Console.WriteLine(prompt);
                isValid = double.TryParse(Console.ReadLine(), out value);

                if (!isValid)
                    Console.WriteLine("Ошибка ввода численного значения");
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
            }
            while (!isValid);

            return value;
        }
        static double SingleMeasurementRoughnessCalculation(double Pin, double Pout, double F)
        {
            double dP = Pin - Pout;
            double lambda = (Math.PI * Math.PI / 8.0) * (D * D * D * D * D) * dP * rho / (L * F * F);
            double Re = (4.0 / Math.PI) * F / (D * viscosity);
            lambda /= 0.11;
            double roughness = lambda * lambda * lambda * lambda - 68.0 / Re;
            return roughness;
        }
    }
}