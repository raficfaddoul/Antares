using System;
using System.Collections.Generic;
using System.Linq;
using System.Data;
using System.Threading.Tasks;

namespace Structural
{
    public class Structure
    {

        public DataTable element { get; set; }
        public DataTable MasseNodale { get; set; }
        public List<double[]> noeud { get; set; }

        public List<Guid> guidsNoeuds { get; set; }

        public List<string[]> CL { get; set; }
        public List<(Guid id, List<int> noeudInd,  double startValue, double endValue)> ChargesStatiqueReparties { get; set; }
        public List<(Guid id, List<int> elementInd, double value, double position)> ChargesStatiquePonctuelles { get; set; }
        public List<(Guid id, List<int> noeudInd, double startValue, double endValue, Dictionary<double,double> tempFunc)> ChargesDynamiqueReparties { get; set; }
        public List<(Guid id, List<int> noeudInd, double value, double position, Dictionary<double, double> tempFunc)> ChargesDynamiquePonctuelles { get; set; }
        public List<(Guid id, List<int> noeudInd, double ValueVL, double ValuePL, double ValueConvoi, double SpeedVL, 
            double SpeedPL, double AverageHourlyTraffic, double PercentagePL, double LoadUncertainty, double SpeedUncertainty)> ChargesMobiles { get; set; }
        
        public int nddl { get; set; }

        public double L { get; set; } = 10;
        public double dx { get; set; } = 5;

        public Structure()
        {
            noeud = new List<double[]>();
            CL = new List<string[]>();

            element = makeElementsDataTable();

            MasseNodale = new DataTable();
            MasseNodale.Columns.Add("Noeud", typeof(int));
            MasseNodale.Columns.Add("Masse", typeof(double));

        }


        static public DataTable makeElementsDataTable()
        {
            DataTable element = new DataTable();            
            element.Columns.Add("Noeudi", typeof(int));
            element.Columns.Add("Noeudj", typeof(int));
            element.Columns.Add("E", typeof(double));
            element.Columns.Add("G", typeof(double));
            element.Columns.Add("S", typeof(double));
            element.Columns.Add("Iy", typeof(double));
            element.Columns.Add("Iz", typeof(double));
            element.Columns.Add("Rho", typeof(double));
            element.Columns.Add("b", typeof(double));
            element.Columns.Add("h", typeof(double));
            element.Columns.Add("Guid", typeof(Guid));

            return element;

        }


        public void LoadStructure(string CaseStudy, int nreElements)
        {
            CL.Clear();
            noeud.Clear();
            MasseNodale.Clear();
            element.Rows.Clear();
            ChargesStatiquePonctuelles.Clear();
            ChargesStatiqueReparties.Clear();
            ChargesDynamiquePonctuelles.Clear();
            ChargesDynamiqueReparties.Clear();
            ChargesMobiles.Clear();

            double E, S, I, Rho;
            double T = 8, b = 0.3, h = 0.6;
            S = b * h;
            I = b * Math.Pow(h, 3) / 12;
            Rho = 2500;
            E = 30000000000;
            L = 10;

            //***************
            /*       S = 0.2;//m2
                   I = 1e-4;//m4
                   E = 200 * 1e9; //N/m2
                   Rho = 15 / S; //kg/m
              */
            //****************

            dx = 5;


            

     // rafic      ChargeStatique.Add(new string[] { "1", "6", "FZ","309.4","1" }); // Cas de charge, N° noeud, label, valeur,N° Fonction Temporelle
        // rafic   ChargeMobile.Add(new string[] { "1", "1", "1.000", "15.000", "44.000", "90", "80",  "500","0.2","0.10","0.20" }) ;// Cas de charge, N° noeud, valeurVL,valeurPL,valeurConvoi, vitesse VL, vitesse PL,AverageHourlyTraffic,tauxPL, aléeCharge, aléaVitesse
        // rafic    FonctionTemporelle.Add(new double[,] { { 0.0, 0.0 }, { 2.0, 1.0 }, { 4.0, 0.2 } });
            

            switch (CaseStudy)
            {
                case "Mini_Maquette":
                    // solution Robot

                    nreElements = 1;

                    S = 0.040 * 0.00165;
                    I = 0.040 * Math.Pow(0.00165, 3) / 12;

                    double Sv = 0.016 * 0.002;
                    double Iv = 0.016 * Math.Pow(0.002, 3) / 12;

                    Rho = 8801/0.981;
                    E = 210000000000;
                    dx = 0.26;
                    h = 0.12;

                    /*      for (int i = 0; i <= 5; i++)
                              noeud.Add(new double[] {  i*0.8, i*0.6});
                          for (int i = 6; i <= 8; i++)
                              noeud.Add(new double[] { i, 3 });

                          for (int i = 1; i <= 8; i++)
                              element.Rows.Add(i - 1, i, E, S, I, Rho);

                          CL.Add(new string[] { "0", "U" });
                          CL.Add(new string[] { "0", "V" });
                          CL.Add(new string[] { "0", "R" });
                      */
                    noeud.Add(new double[] { 0, 0 }); // 0 noeud 1

                    noeud.Add(new double[] { 0.25714, 0 }); // 1 noeud 2
                    noeud.Add(new double[] { 0.25714, h }); // 2 noeud 3

                    noeud.Add(new double[] { 0.51429, 0 }); // 3 noeud 4
                    noeud.Add(new double[] { 0.51429, h }); // 4 noeud 5

                    noeud.Add(new double[] { 0.65000, 0 }); // 5
                    noeud.Add(new double[] { 0.77143, 0 }); // 6 noeud 6
                    noeud.Add(new double[] { 0.77143, h }); // 7 noeud 7

                    noeud.Add(new double[] { 0.90000, 0 }); // 8 noeud 
                    noeud.Add(new double[] { 0.90000, h }); // 9 noeud 

                    noeud.Add(new double[] { 1.02857, 0 }); // 10 noeud 8 
                    noeud.Add(new double[] { 1.02857, h }); // 11 noeud 9

                    noeud.Add(new double[] { 1.28571, 0 }); // 12 noeud 10
                    noeud.Add(new double[] { 1.28571, h }); // 13 noeud 11 

                    noeud.Add(new double[] { 1.54286, 0 }); // 14 noeud 12
                    noeud.Add(new double[] { 1.54286, h }); // 15 noeud 13

                    noeud.Add(new double[] { 1.80000, 0 }); // 16 noeud 14

                    element.Rows.Add(0, 1, E, S, I, Rho);
                    element.Rows.Add(1, 3, E, S, I, Rho);
                    element.Rows.Add(3, 5, E, S, I, Rho);
                    element.Rows.Add(5, 6, E, S, I, Rho);
                    element.Rows.Add(6, 8, E, S, I, Rho);
                    element.Rows.Add(8, 10, E, S, I, Rho);
                    element.Rows.Add(10, 12, E, S, I, Rho);
                    element.Rows.Add(12, 14, E, S, I, Rho);
                    element.Rows.Add(14, 16, E, S, I, Rho);

                    element.Rows.Add(0, 2, E, S, I, Rho);
                    element.Rows.Add(2, 4, E, S, I, Rho);
                    element.Rows.Add(4, 7, E, S, I, Rho);
                    element.Rows.Add(7, 9, E, S, I, Rho);
                    element.Rows.Add(9, 11, E, S, I, Rho);
                    element.Rows.Add(11, 13, E, S, I, Rho);
                    element.Rows.Add(13, 15, E, S, I, Rho);
                    element.Rows.Add(15, 16, E, S, I, Rho);

                    element.Rows.Add(1, 2, E, Sv, Iv, Rho);
                    element.Rows.Add(3, 4, E, Sv, Iv, Rho);
                    element.Rows.Add(6, 7, E, Sv, Iv, Rho);
                    element.Rows.Add(10, 11, E, Sv, Iv, Rho);
                    element.Rows.Add(12, 13, E, Sv, Iv, Rho);
                    element.Rows.Add(14, 15, E, Sv, Iv, Rho);

                    double m = 0.25;
                    MasseNodale.Rows.Add( 2, m);
                    MasseNodale.Rows.Add(4, m );
                    MasseNodale.Rows.Add(7, m );
                    MasseNodale.Rows.Add(11, m );
                    MasseNodale.Rows.Add( 13, m);
                    MasseNodale.Rows.Add(15, m);

                    CL.Add(new string[] { "0", "U" });
                    CL.Add(new string[] { "0", "V" });
                    CL.Add(new string[] { "16", "U" });
                    CL.Add(new string[] { "16", "V" });




                    break;
                case "Poutre_Pont":
                    // solution exacte w1^2 = 827.077 et w2^2 = 10752

                    nreElements = 20;
                    nddl = 21;
                    dx = 5;
                    L = dx * nreElements;

                    // IPE 100
                    // S = 10.32 * 1e-4;
                    // I = 171.0 * 1e-8;

                    // IPE 200
                    S = 28.48 * 1e-4;
                    I = 1943.0 * 1e-8;
                    // IPE 400
                    S = 84.46 * 1e-4;
                    I = 23130 * 1e-8;

                    // HEB 600
                    S = 269.96 * 1e-4;
                    I = 171000 * 1e-8;
                    Rho = 7850;
                    E = 210000000000;
                    for (int i = 0; i <= nreElements; i++)
                        noeud.Add(new double[] { L * (i / ((double)nreElements)), 0 });

                    for (int i = 1; i <= nreElements; i++)
                        element.Rows.Add(i - 1, i, E, S, I, Rho);

                    CL.Add(new string[] { "0", "U" });
                    CL.Add(new string[] { "0", "V" });
                    CL.Add(new string[] { "10", "U" });
                    CL.Add(new string[] { "10", "V" });
                    CL.Add(new string[] { "20", "U" });
                    CL.Add(new string[] { "20", "V" });

                    break;
                case "simple_beam":
                    // solution exacte w1^2 = 827.077 et w2^2 = 10752


                    for (int i = 0; i <= nreElements; i++)
                        noeud.Add(new double[] { L * (i / ((double)nreElements)), 0 });

                    for (int i = 1; i <= nreElements; i++)
                    {
                        double a = 1;
                        if (i == 10) a = 0.85;
                        element.Rows.Add(i - 1, i, E, S, a * I, Rho);
                    }
                    CL.Add(new string[] { "0", "U" });
                    CL.Add(new string[] { "0", "V" });
                    //        CL.Add(new string[] { "0", "R" });
                    CL.Add(new string[] { nreElements.ToString(), "V" });
                    break;

                case "Console":
                    noeud.Add(new double[] { 0, 0 });//0
                    noeud.Add(new double[] { 0.25, 0 });//1
                    noeud.Add(new double[] { 0.50, 0 });//2

                    S = 0.1 * 0.60; I = 0.10 * Math.Pow(0.60, 3) / 12;
                    element.Rows.Add(0, 1, 210000, S, I, 7850);
                    element.Rows.Add(1, 2, 210000, S, I, 7850);

                    CL.Add(new string[] { "0", "U" });
                    CL.Add(new string[] { "0", "V" });
                    CL.Add(new string[] { "0", "R" });
                    break;
                case "fixed_beam":
                    // solution exacte pour 2 éléments : w1^2 = 1.8609 et w2^2 = 24.192

                    for (int i = 0; i <= nreElements; i++)
                        noeud.Add(new double[] { L * (i / ((double)nreElements)), 0 });

                    for (int i = 1; i <= nreElements; i++)
                        element.Rows.Add(i - 1, i, E, S, I, Rho);

                    CL.Add(new string[] { "0", "U" });
                    CL.Add(new string[] { "0", "V" });
                    CL.Add(new string[] { "0", "R" });
                    CL.Add(new string[] { nreElements.ToString(), "U" });
                    CL.Add(new string[] { nreElements.ToString(), "V" });
                    CL.Add(new string[] { nreElements.ToString(), "R" });

                    break;
                case "SimpleFixed_beam":
                    for (int i = 0; i < 12; i++)
                        noeud.Add(new double[] { 0.05 * i, 0 });

                    b = 0.04;
                    h = 0.002;
                    Rho = 7853;
                    E = 2.1e11;
                    S = b * h; I = b * Math.Pow(h, 3) / 12;
                    for (int i = 0; i < 11; i++)
                        element.Rows.Add(i, i + 1, E, S, I, Rho);

                    CL.Add(new string[] { "0", "U" });
                    CL.Add(new string[] { "0", "V" });
                    CL.Add(new string[] { "0", "R" });
                    CL.Add(new string[] { "11", "U" });
                    CL.Add(new string[] { "11", "V" });
                    break;
                case "fixed_beam_3/5":

                    S = 1E-4;//m2
                    I = 1e-7;//m4
                    E = 2 * 1e11; //N/m2
                    Rho = 7860; //kg/m

                    noeud.Add(new double[] { 0, 0 });
                    noeud.Add(new double[] { 0, 3 });
                    noeud.Add(new double[] { 0, 5 });

                    element.Rows.Add(0, 1, E, S, I, Rho);
                    element.Rows.Add(1, 2, E, S, I, Rho);
                    CL.Add(new string[] { "0", "U" });
                    CL.Add(new string[] { "0", "V" });
                    CL.Add(new string[] { "0", "R" });
                    CL.Add(new string[] { "2", "U" });
                    CL.Add(new string[] { "2", "V" });
                    CL.Add(new string[] { "2", "R" });

                    break;
                case "frame":
                    double S_poteau = b * h / 2.0, I_poteau = b * Math.Pow(h / 2.0, 3) / 12;
                    double S_poutre = b * h, I_poutre = b * Math.Pow(h, 3) / 12;

                    for (int i = 0; i <= nreElements; i++)
                        noeud.Add(new double[] { 0, T * (i / ((double)nreElements)) });
                    for (int i = 1; i <= nreElements; i++)
                        noeud.Add(new double[] { L * (i / ((double)nreElements)), T });
                    for (int i = 1; i <= nreElements; i++)
                        noeud.Add(new double[] { L, T - T * (i / ((double)nreElements)) });

                    for (int i = 0; i < nreElements; i++)
                        element.Rows.Add(i, i + 1, E, S_poteau, I_poteau, Rho);

                    for (int i = nreElements; i < 2 * nreElements; i++)
                        element.Rows.Add(i, i + 1, E, S_poutre, I_poutre, Rho);
                    for (int i = 2 * nreElements; i < 3 * nreElements; i++)
                        element.Rows.Add(i, i + 1, E, S_poteau, I_poteau, Rho);


                    CL.Add(new string[] { "0", "U" });
                    CL.Add(new string[] { "0", "V" });
                    CL.Add(new string[] { "0", "R" });
                    CL.Add(new string[] { (3 * nreElements).ToString(), "U" });
                    CL.Add(new string[] { (3 * nreElements).ToString(), "V" });
                    CL.Add(new string[] { (3 * nreElements).ToString(), "R" });
                    break;
                case "DeuxEtages":
                    noeud.Add(new double[] { 0, 0 });//0
                    noeud.Add(new double[] { 0, 6 });//1
                    noeud.Add(new double[] { 8, 6 });//2
                    noeud.Add(new double[] { 8, 0 });//3

                    noeud.Add(new double[] { 0, 12 });//4
                    noeud.Add(new double[] { 8, 12 });//5
                    noeud.Add(new double[] { 16, 12 });//6
                    noeud.Add(new double[] { 16, 6 });//7
                    noeud.Add(new double[] { 16, 0 });//8

                    S_poteau = 0.2 * 0.4; I_poteau = 0.2 * Math.Pow(0.4, 3) / 12; Rho = 0.025;
                    S_poutre = 0.2 * 0.6; I_poutre = 0.2 * Math.Pow(0.6, 3) / 12;
                    element.Rows.Add(0, 1, 33000, S_poteau, I_poteau, Rho);
                    element.Rows.Add(1, 2, 33000, S_poutre, I_poutre, Rho);
                    element.Rows.Add(2, 3, 33000, S_poteau, I_poteau, Rho);

                    element.Rows.Add(1, 4, 33000, S_poteau, I_poteau, Rho);
                    element.Rows.Add(4, 5, 33000, S_poteau, I_poteau, Rho);
                    element.Rows.Add(5, 2, 33000, S_poteau, I_poteau, Rho);
                    element.Rows.Add(5, 6, 33000, S_poteau, I_poteau, Rho);
                    element.Rows.Add(6, 7, 33000, S_poteau, I_poteau, Rho);
                    element.Rows.Add(7, 8, 33000, S_poteau, I_poteau, Rho);
                    element.Rows.Add(2, 7, 33000, S_poteau, I_poteau, Rho);


                    CL.Add(new string[] { "0", "U" });
                    CL.Add(new string[] { "0", "V" });
                    CL.Add(new string[] { "3", "U" });
                    CL.Add(new string[] { "3", "V" });
                    CL.Add(new string[] { "8", "U" });
                    CL.Add(new string[] { "8", "V" });
                    break;
            }
        }
    }

}
