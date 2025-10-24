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
            double[] F = StructuralProcedures.Calcul_F_3D(structure.element, structure.noeud,structure.ChargesStatiquePonctuelles);


            //*** Conditions aux limites ***
            int inoeud, iddl = 0;
            foreach (string[] cl in structure.CL)
            {
                inoeud = iddl = 0;
                if (int.TryParse(cl[0], out inoeud))
                {
                    if (inoeud > (structure.nddl/6) - 1) continue;
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
                }
            }

            MathNet.Numerics.LinearAlgebra.Matrix<double> K1 = MathNet.Numerics.LinearAlgebra.Double.DenseMatrix.OfArray(K);
            double dt = K1.Determinant();
            if (dt>1e-20)
                K1 = K1.Inverse();

            for (int i = 0; i < structure.nddl; i++)
            {
                double s = 0;
                for (int j = 0; j < structure.nddl; j++)
                    s += K1[i, j] * F[j];
                q[i] = s;
            }

            foreach (DataRow elem in structure.element.Rows)
            {
                double[] Fei = StructuralProcedures.Fe_Beam3D(elem, structure.noeud, q);
                Fe.Add(Fei);
            }


            // 12 solicitations par elements
            // 6 ddl par noeuds

        }


    }
}
