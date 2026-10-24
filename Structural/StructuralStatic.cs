using Accord;
using AliantWebS.Models;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Security;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace Structural
{
    public static class StructuralStatic
    {


        static public void StructureVerification(Structural.Structure structure, double[] q, double[] FeMax)
        {
            //************* Provisoire : début  *******************
            /*
                        structure = new Structure();

                        structure.noeud.Add([0, 0, 0]);
                        structure.noeud.Add([0, 0, 2]);
                        structure.noeud.Add([-3, 4, 2]);
                       // structure.noeud.Add([10, 2, 2]);

                        structure.element.Rows.Add(0, 1, 12000, 5454, 0.5 * 1, 0.5 * 1 / 12, 1 * 0.5 / 12, 0.5, 1, 0.5, Guid.Empty);
                        structure.element.Rows.Add(1, 2, 12000, 5454, 0.5 * 1, 0.5 * 1 / 12, 1 * 0.5 / 12, 0.5, 1, 0.5, Guid.Empty);
                       // structure.element.Rows.Add(2, 3, 12000, 5454, 0.5 * 1, 0.5 * 1 / 12, 1 * 0.5 / 12, 0.5, 1, 0.5, Guid.Empty);


                        structure.nddl = 6 * structure.noeud.Count();

                        structure.CL.Add(new string[] { "0", "U" });
                        structure.CL.Add(new string[] { "0", "V" });
                        structure.CL.Add(new string[] { "0", "W" });
                        structure.CL.Add(new string[] { "0", "RX" });
                        structure.CL.Add(new string[] { "0", "RY" });
                        structure.CL.Add(new string[] { "0", "RZ" });


            */

        /*    structure = new Structure();

            structure.noeud.Add([0, -3, 0]);
            structure.noeud.Add([4, 0, 0]);
            structure.noeud.Add([0, 3, 0]);
            structure.noeud.Add([-4, 0, 0]);
            structure.noeud.Add([6, 3, 5]);

            structure.element.Rows.Add(0, 4, 12000, 5000, 0.3 * 1.2, 0.3 * 1.2 * 1.2 * 1.2 / 12, 1.2 * 0.3 * 0.3 * 0.3 / 12, 700, 1.2, 0.3, Guid.Empty);
            structure.element.Rows.Add(1, 4, 12000, 5000, 0.3 * 1.2, 0.3 * 1.2 * 1.2 * 1.2 / 12, 1.2 * 0.3 * 0.3 * 0.3 / 12, 700, 1.2, 0.3, Guid.Empty);
            structure.element.Rows.Add(2, 4, 12000, 5000, 0.3 * 1.2, 0.3 * 1.2 * 1.2 * 1.2 / 12, 1.2 * 0.3 * 0.3 * 0.3 / 12, 700, 1.2, 0.3, Guid.Empty);
            structure.element.Rows.Add(3, 4, 12000, 5000, 0.3 * 1.2, 0.3 * 1.2 * 1.2 * 1.2 / 12, 1.2 * 0.3 * 0.3 * 0.3 / 12, 700, 1.2, 0.3, Guid.Empty);

            structure.nddl = 6 * structure.noeud.Count();

            for (int i = 0; i < 4; i++)
            {
                structure.CL.Add(new string[] { i.ToString(), "U" });
                structure.CL.Add(new string[] { i.ToString(), "V" });
                structure.CL.Add(new string[] { i.ToString(), "W" });
            }
        */
            //************* Provisoire : fin  *********************






            List<double[]> Fe = new List<double[]>();
            StaticAnalysis(structure, out q, out Fe);

            int nbelem = -1;
            foreach (double[] Fei in Fe)
            {
                nbelem++;
                for (int i = 0; i < 6; i++)
                {
                    if (Math.Abs(Fei[i]) > Math.Abs(Fei[i + 6])) FeMax[i + 6 * nbelem] = Math.Abs(Fei[i]); 
                    else FeMax[i + 6 * nbelem] = Math.Abs(Fei[i + 6]); ;
                }
            }


            // 6 solicitations par elements
            // 6 ddl par noeuds
        }
        static public void StaticAnalysis(Structural.Structure structure, out double[] q, out List<double[]> Fe)
        {
            q = new double[structure.nddl];
            Fe = new List<double[]>();

            double[,] K = StructuralProcedures.Calcul_K_3D(structure.element, structure.noeud);
            double[] F = StructuralProcedures.Calcul_F_3D(structure.element, structure.noeud, structure.ChargesStatiquePonctuelles);
            // *Provisoire
            //double[] F = new double[structure.nddl];
            //F[12] = 10;

            //F[6*4+2] = 100;

            /*
            int[,] tabNodes =
                {
                {117,118,119,120,121,122,123,124,125 },//axe F
                {108,109,110,111,112,113,114,115,116 },
                {99,100,101,102,103,104,105,106,107 },//axe E
                {90,91,92,93,94,95,96,97,98 },
                {81,82,83,84,85,86,87,88,89 },//axe D
                {72,73,74,75,76,77,78,79,80},
                {63,64,65,66,67,69,69,70,71 }, //axe C
                {54,55,56,57,58,59,60,61,62 },
                {45,46,47,48,49,50,51,52,53 } //axe B
            };
            double[,] tabCharges =
     {
                {0 ,  0 ,      0,   0,      0,   0,   0,   0,  0 },//axe F
                {0 ,  0 ,      0,   0,      0,   0,   0,   0,  0 },
                {0 ,  0 ,      0,   0,    100,   0,   0,   0,  0 },//axe E
                {0 ,  0 ,      0,   0,      0,   0,   0,   0,  0 },
                {0 ,  0 ,      0,   0,    100,   0,   0,   0,  0 },//axe D
                {0 ,  0 ,      0,   0,      0,   0,   0,   0,  0 },
                {0 ,  0 ,      0,   0,    100,   0,   0,   0,  0 },//axe C
                {0 ,  0 ,      0,   0,      0,   0,   0,   0,  0 },
                {0 ,  0 ,      0,   0,      0,   0,   0,   0,  0 }//axe B
            };

            for (int i = 0; i < 9; i++)
                for (int j = 0; j < 9; j++)
                    F[6 * tabNodes[i, j] + 2] = tabCharges[i, j];
            */
            

            //*** Conditions aux limites ***
            int inoeud, iddl = 0;
            string sf = "";
            for (int i = 0; i < structure.nddl; i++)
            {
                if (Math.Abs(F[i]) > 0) sf += i.ToString() + ";" + F[i].ToString() + "\n";
            }
                foreach (string[] cl in structure.CL)
            {
                inoeud = iddl = 0;
                if (int.TryParse(cl[0], out inoeud))
                {
                    if (inoeud > (structure.nddl / 6) - 1) continue;
                    switch (cl[1].ToUpper())
                    {
                        case "U":
                            iddl = 0; break;
                        case "V":
                            iddl = 1; break;
                        case "W":
                            iddl = 2; break;
                        case "RX":
                            iddl = 3; break;
                        case "RY":
                            iddl = 4; break;
                        case "RZ":
                            iddl = 5; break;
                        default:
                            iddl = 0; break;
                    }
                    for (int i = 0; i < structure.nddl; i++)
                        K[i, 6 * inoeud + iddl] = K[6 * inoeud + iddl, i] = 0;
                    K[6 * inoeud + iddl, 6 * inoeud + iddl] = 1;
                    F[6 * inoeud + iddl] = 0;
                    //Blocage des rotations
                    /*     for (int ir = 3; ir <= 5; ir++)
                         {
                             for (int i = 0; i < structure.nddl; i++)
                                 K[i, 6 * inoeud + ir] = K[6 * inoeud + ir, i] = 0;
                             K[6 * inoeud + ir, 6 * inoeud + ir] = 1;
                         }
                    */
                }
            }
            /* for (int ind = 10; ind < 261; ind++)
                 for (int ir = 0; ir <= 5; ir++)
                 {
                     for (int i = 0; i < structure.nddl; i++)
                     K[i, 6 * ind + ir] = K[6 * ind + ir, i] = 0;
                 K[6 * ind + ir, 6 * ind + ir] = 1;
             }


             for (int i = 0; i < structure.nddl; i++)
             {
                 if (Math.Abs(K[i, i]) < 1e-10)
                 {
                     int korp = 0;
                 }
             }
            */
       /*     string s1 = "";
            for (int i = 0; i < structure.nddl; i += 6)
            {
                s1 += (i / 6).ToString() + ";" + K[i, i].ToString() + ";" + K[i + 1, i + 1].ToString() + ";" + K[i + 2, i + 2].ToString() + ";" + K[i + 3, i + 3].ToString() + ";" + K[i + 4, i + 4].ToString() + ";" + K[i + 5, i + 5].ToString() + "\n";
            }
            string hs1 = s1;

            s1 = "";
            for (int i = 0; i < structure.nddl; i++)
            {
                for (int j = 0; j < structure.nddl; j++)
                {
                    s1 += K[i, j].ToString("N2") + ";";
                }
                s1 += "\n";
            }
            hs1 = s1;
       */
            // Un ddl sans rigidité (noeud relié à aucun élément, élément de longueur nulle...) rend K singulière
            for (int i = 0; i < structure.nddl; i++)
            {
                if (double.IsNaN(K[i, i]) || Math.Abs(K[i, i]) < 1e-12)
                    throw new InvalidOperationException($"Ddl {i % 6} du noeud {i / 6} sans rigidité (noeud non relié ou élément dégénéré).");
            }

            // Résolution K q = F par Cholesky (K symétrique définie positive).
            // Ne pas tester le déterminant : pour ~2000 ddl il vaut ~10^4000 et déborde en Infinity,
            // ou tombe à 0, alors que la matrice est parfaitement inversible.
            var K1 = MathNet.Numerics.LinearAlgebra.Double.DenseMatrix.OfArray(K);
            var F1 = MathNet.Numerics.LinearAlgebra.Double.DenseVector.OfArray(F);
            MathNet.Numerics.LinearAlgebra.Vector<double> q1;
            try
            {
                q1 = K1.Cholesky().Solve(F1);
            }
            catch (ArgumentException)
            {
                throw new InvalidOperationException("Matrice de rigidité non définie positive : structure instable (mécanisme ou appuis insuffisants).");
            }
            for (int i = 0; i < structure.nddl; i++)
                q[i] = q1[i]/1000;

            foreach (DataRow elem in structure.element.Rows)
            {
                double[] Fei = StructuralProcedures.Fe_Beam3D(elem, structure.noeud, q);
                Fe.Add(Fei);
            }

            //string sq = "";
            //for (int i = 0; i < structure.nddl / 6; i++)
            //    sq += i.ToString() + ";" + q[6 * i + 0].ToString() + ";" + q[6 * i + 1].ToString() + ";" + q[6 * i + 2].ToString() + "\n";
            
            // 12 solicitations par elements
            // 6 ddl par noeuds

        }


    }
}
