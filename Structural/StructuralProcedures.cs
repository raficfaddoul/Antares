using AliantWebS.Models;
using DocumentFormat.OpenXml.EMMA;
using MathNet.Numerics.LinearAlgebra.Factorization;
using NetTopologySuite.Algorithm;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Structural
{
    public static class StructuralProcedures
    {

        static private double[,] Calcul_Ke(DataRow elem, List<double[]> noeud)
        {
            double xi = noeud[elem.Field<int>("Noeudi")][0];
            double yi = noeud[elem.Field<int>("Noeudi")][1];
            double xj = noeud[elem.Field<int>("Noeudj")][0];
            double yj = noeud[elem.Field<int>("Noeudj")][1];
            double E = elem.Field<double>("E");
            double S = elem.Field<double>("S");
            double I = elem.Field<double>("I");

            double L = Math.Sqrt(Math.Pow(xj - xi, 2) + Math.Pow(yj - yi, 2));
            double L2 = L * L;
            double L3 = L * L * L;

            double[,] Ke =
            {
                { E*S/L, 0        , 0       ,-E*S/L, 0        , 0        },
                { 0    , 12*E*I/L3, 6*E*I/L2, 0    ,-12*E*I/L3, 6*E*I/L2 },
                { 0    , 6*E*I/L2 , 4*E*I/L , 0    ,-6*E*I/L2 , 2*E*I/L  },
                {-E*S/L, 0        , 0       , E*S/L, 0        , 0        },
                { 0    ,-12*E*I/L3,-6*E*I/L2, 0    , 12*E*I/L3,-6*E*I/L2 },
                { 0    , 6*E*I/L2 , 2*E*I/L , 0    ,-6*E*I/L2 , 4*E*I/L  }
            };
            return Ke;
        }
        static private double[,] Calcul_Me(DataRow elem, List<double[]> noeud)
        {
            double xi = noeud[elem.Field<int>("Noeudi")][0];
            double yi = noeud[elem.Field<int>("Noeudi")][1];
            double xj = noeud[elem.Field<int>("Noeudj")][0];
            double yj = noeud[elem.Field<int>("Noeudj")][1];
            double rho = elem.Field<double>("Rho");
            double S = elem.Field<double>("S");

            double L = Math.Sqrt(Math.Pow(xj - xi, 2) + Math.Pow(yj - yi, 2));
            double L2 = L * L;
            double L3 = L * L * L;

            double c = rho * S * L / 420;
            double[,] Me =
            {
                { 210*c, 0     , 0     ,-210*c, 0     , 0      },
                { 0    , 156*c , 22*L*c, 0    , 54*c  ,-13*L*c },
                { 0    , 22*L*c, 4*L2*c, 0    , 13*L*c,-3*L2*c },
                {-210*c, 0     , 0     , 210*c, 0     , 0      },
                { 0    , 54*c  , 13*L*c, 0    , 156*c ,-22*L*c },
                { 0    ,-13*L*c,-3*L2*c, 0    ,-22*L*c, 4*L2*c }
            };
            return Me;
        }


        static public double[,] Calcul_K(DataTable element, List<double[]> noeud)
        {
            double[,] K = new double[3 * noeud.Count(), 3 * noeud.Count()];
            foreach (DataRow elem in element.Rows)
            {
                double[,] Ke = Calcul_Ke(elem, noeud);
                Ke = Rotation(elem, noeud, Ke);
                Assemblage(K, Ke, elem);
            }


            return K;
        }
        static public double[,] Calcul_M(DataTable element, List<double[]> noeud)
        {
            double[,] M = new double[3 * noeud.Count(), 3 * noeud.Count()];
            foreach (DataRow elem in element.Rows)
            {
                double[,] Me = Calcul_Me(elem, noeud);
                Me = Rotation(elem, noeud, Me);
                Assemblage(M, Me, elem);
            }
            return M;
        }

        static public double[,] Calcul_MNoeud(DataTable MasseNodale, double[,] M)
        {
            for (int i = 0; i < MasseNodale.Rows.Count; i++)
            {
                int n = MasseNodale.Rows[i].Field<int>("Noeud");
                double m = MasseNodale.Rows[i].Field<double>("Masse");
                M[3 * n + 0, 3 * n + 0] += m;
                M[3 * n + 1, 3 * n + 1] += m;
            }
            return M;
        }


        static private void Assemblage(double[,] A, double[,] Ag, DataRow elem)
        {
            int i, j, ik, jk;
            int[] n = new int[2];
            n[0] = elem.Field<int>("Noeudi");
            n[1] = elem.Field<int>("Noeudj");
            for (ik = 0; ik < 2; ik++)
                for (jk = 0; jk < 2; jk++)
                {
                    for (i = 0; i < 3; i++)
                        for (j = 0; j < 3; j++)
                        {
                            A[3 * n[ik] + i, 3 * n[jk] + j] += Ag[3 * ik + i, 3 * jk + j];
                        }
                }

        }

        static public double[,] Matrice_R(DataRow elem, List<double[]> noeud)
        {
            double xi = noeud[elem.Field<int>("Noeudi")][0];
            double yi = noeud[elem.Field<int>("Noeudi")][1];
            double xj = noeud[elem.Field<int>("Noeudj")][0];
            double yj = noeud[elem.Field<int>("Noeudj")][1];
            double E = elem.Field<double>("Rho");

            double L = Math.Sqrt(Math.Pow(xj - xi, 2) + Math.Pow(yj - yi, 2));
            double c = (xj - xi) / L;
            double s = (yj - yi) / L;

            double[,] R =
             {
                { c, s, 0, 0,0,0 },
                {-s, c, 0, 0,0,0 },
                { 0, 0, 1, 0,0,0 },
                { 0, 0, 0, c,s,0 },
                { 0, 0, 0,-s,c,0 },
                { 0, 0, 0, 0,0,1 }
            };

            return R;

        }

        static private double[,] Rotation(DataRow elem, List<double[]> noeud, double[,] A)
        {
            double[,] Ag = new double[6, 6];
            double[,] AR = new double[6, 6];

            double[,] R = Matrice_R(elem, noeud);

            int i, j, k;
            double u = 0;
            for (i = 0; i < 6; i++)
                for (j = 0; j < 6; j++)
                {
                    u = 0;
                    for (k = 0; k < 6; k++) u += A[i, k] * R[k, j];
                    AR[i, j] = u;
                }

            for (i = 0; i < 6; i++)
                for (j = 0; j < 6; j++)
                {
                    u = 0;
                    for (k = 0; k < 6; k++) u += R[k, i] * AR[k, j];
                    Ag[i, j] = u;
                }


            return Ag;
        }


        static public double[] Rotation_qe(DataRow elem, List<double[]> noeud, double[] qeG)
        {
            double[] qeL = new double[6];

            double[,] R = Matrice_R(elem, noeud);

            int i, j;
            double u = 0;
            for (i = 0; i < 6; i++)
            {
                u = 0;
                for (j = 0; j < 6; j++)
                    u += R[j, i] * qeG[j];
                qeL[i] = u;
            }
            return qeL;
        }


        static private double[,] Calcul_Ke_Beam3D(DataRow elem, List<double[]> noeud)
        {
            double xi, yi, zi, xj, yj, zj, b, h;
            int i = elem.Field<int>("Noeudi");
            int j = elem.Field<int>("Noeudj");
  
            xi = noeud[i][0];
            yi = noeud[i][1];
            zi = noeud[i][2];
            xj = noeud[j][0];
            yj = noeud[j][1];
            zj = noeud[j][2];

            b = elem.Field<double>("b");
            h = elem.Field<double>("h");

            double E = elem.Field<double>("E"); // MPa = MN/m²
            double S = b*h;
            double Iy = b * Math.Pow(h, 3) / 12.0;
            double Iz = h * Math.Pow(b, 3) / 12.0;
            double nu = 0.1;
            double G = E / (2*(1 + nu));
            double J = Iy + Iz;

            double L = Math.Sqrt(Math.Pow(xj - xi, 2) + Math.Pow(yj - yi, 2) + Math.Pow(zj - zi, 2));
            double L2 = L * L;
            double L3 = L * L * L;


            if (L<0.1 || L>12 || E<1000 || S<1e-5)
            {
                int hh = 0;

            }

            double[,] Ke =
            {
                { E*S/L, 0         , 0         , 0       , 0        , 0        ,-E*S/L   , 0         , 0         , 0       , 0        , 0         },
                { 0    , 12*E*Iz/L3, 0         , 0       , 0        , 6*E*Iz/L2, 0       ,-12*E*Iz/L3, 0         , 0       , 0        , 6*E*Iz/L2 },
                { 0    , 0         , 12*E*Iy/L3, 0       ,-6*E*Iy/L2, 0        , 0       , 0         ,-12*E*Iy/L3, 0       ,-6*E*Iy/L2, 0         },
                { 0    , 0         , 0         , G*J/L   , 0        , 0        , 0       , 0         , 0         ,-G*J/L   , 0        , 0         },
                { 0    , 0         ,-6*E*Iy/L2 , 0       , 4*E*Iy/L , 0        , 0       , 0         , 6*E*Iy/L2 , 0       , 2*E*Iy/L , 0         },
                { 0    , 6*E*Iz/L2 , 0         , 0       , 0        , 4*E*Iz/L , 0       ,-6*E*Iz/L2 , 0         , 0       , 0        , 2*E*Iz/L  },
                {-E*S/L, 0         , 0         , 0       , 0        , 0        , E*S/L   , 0         , 0         , 0       , 0        , 0         },
                { 0    ,-12*E*Iz/L3, 0         , 0       , 0        ,-6*E*Iz/L2, 0       , 12*E*Iz/L3, 0         , 0       , 0        ,-6*E*Iz/L2 },
                { 0    , 0         ,-12*E*Iy/L3, 0       , 6*E*Iy/L2, 0        , 0       , 0         , 12*E*Iy/L3, 0       , 6*E*Iy/L2, 0         },
                { 0    , 0         , 0         ,-G*J/L   , 0        , 0        , 0       , 0         , 0         , G*J/L   , 0        , 0         },
                { 0    , 0         ,-6*E*Iy/L2 , 0       , 2*E*Iy/L , 0        , 0       , 0         , 6*E*Iy/L2 , 0       , 4*E*Iy/L , 0         },
                { 0    , 6*E*Iz/L2 , 0         , 0       , 0        , 2*E*Iz/L , 0       ,-6*E*Iz/L2 , 0         , 0       , 0        , 4*E*Iz/L  }
            };

            return Ke;
        }
        static private double[,] Calcul_Me_Beam3D(DataRow elem, List<double[]> noeud)
        {
            double xi = noeud[elem.Field<int>("Noeudi")][0];
            double yi = noeud[elem.Field<int>("Noeudi")][1];
            double xj = noeud[elem.Field<int>("Noeudj")][0];
            double yj = noeud[elem.Field<int>("Noeudj")][1];
            double rho = elem.Field<double>("Rho");
            double S = elem.Field<double>("S");

            double L = Math.Sqrt(Math.Pow(xj - xi, 2) + Math.Pow(yj - yi, 2));
            double L2 = L * L;
            double L3 = L * L * L;

            double c = rho * S * L / 420;
            double[,] Me =
            {
                { 210*c, 0     , 0     ,-210*c, 0     , 0      },
                { 0    , 156*c , 22*L*c, 0    , 54*c  ,-13*L*c },
                { 0    , 22*L*c, 4*L2*c, 0    , 13*L*c,-3*L2*c },
                {-210*c, 0     , 0     , 210*c, 0     , 0      },
                { 0    , 54*c  , 13*L*c, 0    , 156*c ,-22*L*c },
                { 0    ,-13*L*c,-3*L2*c, 0    ,-22*L*c, 4*L2*c }
            };
            return Me;
        }


        static public double[,] Calcul_K_3D(DataTable element, List<double[]> noeud)
        {
            double[,] K = new double[6 * noeud.Count(), 6 * noeud.Count()];
            foreach (DataRow elem in element.Rows)
            {
                double[,] Ke = Calcul_Ke_Beam3D(elem, noeud);
                Ke = Rotation_Beam3D(elem, noeud, Ke);
                Assemblage_Beam3D(K, Ke, elem);

                for (int i = 0; i < 12; i++)
                {
                    if (Math.Abs(Ke[i, i]) < 1e-10)
                    {
                        int korp = 0;
                    }
                }
            }


            for (int i = 0; i < (6*noeud.Count()); i++)
            {
                if (Math.Abs(K[i, i]) < 1e-10)
                {
                    int korp = 0;
                }
            }

            return K;
        }
        static public double[,] Calcul_M_3D(DataTable element, List<double[]> noeud)
        {
            double[,] M = new double[3 * noeud.Count(), 3 * noeud.Count()];
            foreach (DataRow elem in element.Rows)
            {
                double[,] Me = Calcul_Me(elem, noeud);
                Me = Rotation(elem, noeud, Me);
                Assemblage(M, Me, elem);
            }
            return M;
        }

        static public double[,] Calcul_MNoeud_3D(DataTable MasseNodale, double[,] M)
        {
            for (int i = 0; i < MasseNodale.Rows.Count; i++)
            {
                int n = MasseNodale.Rows[i].Field<int>("Noeud");
                double m = MasseNodale.Rows[i].Field<double>("Masse");
                M[3 * n + 0, 3 * n + 0] += m;
                M[3 * n + 1, 3 * n + 1] += m;
            }
            return M;
        }
        static public double[] Calcul_F_3D
            (DataTable element, List<double[]> noeud, List<(Guid id, List<int> noeudInd, int dof, double value, double position)> ChargesStatiquePonctuelles)
        {
            double[] F = new double[6 * noeud.Count()];


            foreach (var charge in ChargesStatiquePonctuelles)
            {
                double P = charge.value;
                for (int j = 0; j < charge.noeudInd.Count; j++)
                    F[6 * charge.noeudInd[j] + 1] += P/100;
            }
            return F;
        }


        static private void Assemblage_Beam3D(double[,] A, double[,] Ag, DataRow elem)
        {
            int i, j, ik, jk;
            int[] n = new int[2];
            n[0] = elem.Field<int>("Noeudi");
            n[1] = elem.Field<int>("Noeudj");
  

            for (ik = 0; ik < 2; ik++)
                for (jk = 0; jk < 2; jk++)
                {
                    for (i = 0; i < 6; i++)
                        for (j = 0; j < 6; j++)
                        {
                            A[6 * n[ik] + i, 6 * n[jk] + j] += Ag[6 * ik + i, 6 * jk + j];
                        }
                }

        }

        static public double[,] Matrice_R_Beam3D(DataRow elem, List<double[]> noeud)
        {
            double xi, yi, zi, xj, yj, zj;

            int i = elem.Field<int>("Noeudi");
            int j = elem.Field<int>("Noeudj");

            xi = noeud[i][0];
            yi = noeud[i][1];
            zi = noeud[i][2];
            xj = noeud[j][0];
            yj = noeud[j][1];
            zj = noeud[j][2];

            double dx = (xj - xi);
            double dy = (yj - yi);
            double dz = (zj - zi);

            double L = Math.Sqrt(dx * dx + dy * dy + dz * dz);

            double xk,yk, zk;
            //pente de l'élément dans le plan vertical
            double Lxy = Math.Sqrt(Math.Pow(xj - xi, 2) + Math.Pow(yj - yi, 2));
            if (Lxy == 0)
            {
                // element vertical =>  point "o" dans le plan (x,z)
                xk = xi - 10;
                yk = yi ;
                zk = zi;
            }
            else
            {
                // sinon =>  point "o" dans le plan vertical de l'élément
                double cosX = (xj - xi) / Lxy;
                double cosY = (yj - yi) / Lxy;
                double Emme = (zj - zi) / Lxy;
                zk = zi+ 10;
                double x1o = -10 * Emme;
                xk = xi + x1o * cosX;
                yk = yi + x1o * cosY;
            }

            // Vecteur e1
            double[] e1 = new[] { dx / L, dy / L, dz / L };

            //Vecteur u : produit de e1 par "v" 
            double[] v= new[] { (xk-xi) , (yk - yi), (zk - zi) };
            double[] u=new double[3];
            u[0] = e1[1] * v[2] - e1[2] * v[1];
            u[1] = e1[2] * v[0] - e1[0] * v[2];
            u[2] = e1[0] * v[1] - e1[1] * v[0];

            //Vecteur e3 : produit de u par e1 
            double[] e3 = new double[3];

            e3[0] = u[1] * e1[2] - u[2] * e1[1];
            e3[1] = u[2] * e1[0] - u[0] * e1[2];
            e3[2] = u[0] * e1[1] - u[1] * e1[0];

            double norme_e3 = Math.Sqrt(e3[0] * e3[0] + e3[1] * e3[1] + e3[2] * e3[2]);
            e3[0] /= norme_e3;
            e3[1] /= norme_e3;
            e3[2] /= norme_e3;
            
            //produit de e3 x e1
            double[] e2 = new double[3];
            e2[0] = e3[1] * e1[2] - e3[2] * e1[1];
            e2[1] = e3[2] * e1[0] - e3[0] * e1[2];
            e2[2] = e3[0] * e1[1] - e3[1] * e1[0];


            double[,] lambda = new double[3, 3]
            {
                { e1[0], e1[1], e1[2] },
                { e2[0], e2[1], e2[2] },
                { e3[0], e3[1], e3[2] }
            };

      
            double[,] R = new double[12, 12];

            for (int nd = 0; nd < 4; nd++)
            {
                int offset = nd * 3;
                for (i = 0; i < 3; i++)
                    for (j = 0; j < 3; j++)
                        R[offset + i, offset + j] = lambda[i, j];
            }
            return R;

        }
        static public double[,] Matrice_R_Beam3D_WORKING_BUT_TOGENERALIZE(DataRow elem, List<double[]> noeud)
        {
            double xi, yi, zi, xj, yj, zj;

            int i = elem.Field<int>("Noeudi");
            int j = elem.Field<int>("Noeudj");

            xi = noeud[i][0];
            yi = noeud[i][1];
            zi = noeud[i][2];
            xj = noeud[j][0];
            yj = noeud[j][1];
            zj = noeud[j][2];

            double dx = (xj - xi);
            double dy = (yj - yi);
            double dz = (zj - zi);

            double L = Math.Sqrt(dx * dx + dy * dy + dz * dz);
            double[] e1 = new[] { dx / L, dy / L, dz / L };

            // --- Étape 2 : Vecteur de référence V ---
            double[] V = Math.Abs(e1[2]) < 0.9 ?
                new[] { 0.0, 0.0, -1.0 } :  // Si poutre n'est pas verticale → V = Z
                new[] { 1.0, 0.0, 0.0 };   // Si poutre est verticale → V = Y

            // --- Étape 3 : e3 = e1 × V (produit vectoriel) ---
            double[] e2 = new[]
            {
        e1[1]*V[2] - e1[2]*V[1],
        e1[2]*V[0] - e1[0]*V[2],
        e1[0]*V[1] - e1[1]*V[0]
            };
            double norm_e2 = Math.Sqrt(e2[0] * e2[0] + e2[1] * e2[1] + e2[2] * e2[2]);

            // Si e1 et V sont parallèles, norm_e3 ≈ 0 → changer V
            if (norm_e2 < 0.01)
            {
                V = new[] { 0.0, -1.0, 0.0 };
                e2 = new[] { e1[1] * V[2] - e1[2] * V[1], e1[2] * V[0] - e1[0] * V[2], e1[0] * V[1] - e1[1] * V[0] };
                norm_e2 = Math.Sqrt(e2[0] * e2[0] + e2[1] * e2[1] + e2[2] * e2[2]);
            }

            e2[0] /= norm_e2;
            e2[1] /= norm_e2;
            e2[2] /= norm_e2;

            // --- Étape 4 : e2 = e3 × e1 ---
            double[] e3 = new[]
            {
        e1[1]*e2[2] - e1[2]*e2[1],
        e1[2]*e2[0] - e1[0]*e2[2],
        e1[0]*e2[1] - e1[1]*e2[0]
    };


            // --- Étape 5 : Matrice de cosinus directeurs (3×3) ---
            double[,] lambda = new double[3, 3]
            {
                { e1[0], e1[1], e1[2] },
                { e2[0], e2[1], e2[2] },
                { e3[0], e3[1], e3[2] }
            };

            // --- Étape 6 : Construire T (12×12) ---
            double[,] R = new double[12, 12];

            // Remplir avec les blocs [λ] pour les 2 nœuds
            for (int nd = 0; nd < 2; nd++)
            {
                int offset = nd * 6;

                // Bloc translations (3×3)
                for (i = 0; i < 3; i++)
                    for (j = 0; j < 3; j++)
                        R[offset + i, nd * 6 + j] = lambda[i, j];

                // Bloc rotations (3×3)
                for (i = 0; i < 3; i++)
                    for (j = 0; j < 3; j++)
                        R[offset + 3 + i, nd * 6 + 3 + j] = lambda[i, j];
            }



            return R;

        }
        static public double[,] Matrice_R_Beam3D_OLD(DataRow elem, List<double[]> noeud)
        {
            double xi, yi, zi, xj, yj, zj;

            int i = elem.Field<int>("Noeudi");
            int j = elem.Field<int>("Noeudj");

            xi = noeud[i][0];
            yi = noeud[i][1];
            zi = noeud[i][2];
            xj = noeud[j][0];
            yj = noeud[j][1];
            zj = noeud[j][2];

            double L = Math.Sqrt(Math.Pow(xj - xi, 2) + Math.Pow(yj - yi, 2) + Math.Pow(zj - zi, 2));
            double lx = (xj - xi) / L;
            double mx = (yj - yi) / L;
            double nx = (zj - zi) / L;

            double alpha,beta=0,gamma=0;
            double ly, my, ny, lz, mz, nz;
            double Lx = xj - xi;
            double Lxy = Math.Sqrt(Math.Pow(xj - xi, 2) + Math.Pow(yj - yi, 2));
            alpha = 0;
            if (Math.Abs(Lxy) > 0)
            {
                beta = Math.Acos(Lxy / L);
                if (Math.Abs(Lx) > 0)
                {
                    gamma = Math.Acos(Lx / Lxy);                   
                }
                else
                {
                    gamma = Math.PI / 2;
                }
            }
            else
            {
                beta = Math.PI / 2;
            }

            ly = -Math.Sin(alpha) * Math.Sin(beta) * Math.Cos(gamma) - Math.Cos(alpha) * Math.Sin(gamma);
            my = -Math.Sin(alpha) * Math.Sin(beta) * Math.Sin(gamma) + Math.Cos(alpha) * Math.Cos(gamma);
            ny = Math.Sin(alpha) * Math.Cos(beta);


            lz = mx * ny - nx * my;
            mz = nx * ly - lx * ny;
            nz = lx * my - mx * ly ;

            double[,] R =
             {
                {lx,mx,nx, 0, 0, 0, 0, 0, 0, 0, 0, 0 },
                {ly,my,ny, 0, 0, 0, 0, 0, 0, 0, 0, 0 },
                {lz,mz,nz, 0, 0, 0, 0, 0, 0, 0, 0, 0 },
                { 0, 0, 0,lx,mx,nx, 0, 0, 0, 0, 0, 0 },
                { 0, 0, 0,ly,my,ny, 0, 0, 0, 0, 0, 0 },
                { 0, 0, 0,lz,mz,nz, 0, 0, 0, 0, 0, 0 },
                { 0, 0, 0, 0, 0, 0,lx,mx,nx, 0, 0, 0 },
                { 0, 0, 0, 0, 0, 0,ly,my,ny, 0, 0, 0 },
                { 0, 0, 0, 0, 0, 0,lz,mz,nz, 0, 0, 0 },
                { 0, 0, 0, 0, 0, 0, 0, 0, 0,lx,mx,nx },
                { 0, 0, 0, 0, 0, 0, 0, 0, 0,ly,my,ny },
                { 0, 0, 0, 0, 0, 0, 0, 0, 0,lz,mz,nz }
            };

            return R;

        }



        static private double[,] Rotation_Beam3D(DataRow elem, List<double[]> noeud, double[,] A)
        {
            double[,] Ag = new double[12, 12];
            double[,] AR = new double[12, 12];

            double[,] R = Matrice_R_Beam3D(elem, noeud);

            int i, j, k;
            double u = 0;
            for (i = 0; i < 12; i++)
                for (j = 0; j < 12; j++)
                {
                    u = 0;
                    for (k = 0; k < 12; k++) u += A[i, k] * R[k, j];
                    AR[i, j] = u;
                }

            for (i = 0; i < 12; i++)
                for (j = 0; j < 12; j++)
                {
                    u = 0;
                    for (k = 0; k < 12; k++) u += R[k, i] * AR[k, j];
                    Ag[i, j] = u;
                }

            for (i = 0; i < 12; i++)
                for (j = 0; j < 12; j++)
                {
                    if  (double.IsNaN(Ag[i, j]))
                        {
                        int korp = 0;
                    }
                }

                    return Ag;
        }


        static public double[] qe_Beam3D(DataRow elem, List<double[]> noeud, double[] q)
        {
            int ni = elem.Field<int>("Noeudi");
            int nj = elem.Field<int>("Noeudj");
      

            double[] qeG = new double[12]; 
            for (int i = 0; i < 6; i++)
            {
                qeG[i] = q[6 * ni + i];
                qeG[i+6] = q[6 * nj + i];
            }

            double[] qe = Rotation_qe_Beam3D(elem, noeud, qeG);

            return qe;

        }

        static public double[] Fe_Beam3D(DataRow elem, List<double[]> noeud, double[] q)
        {
            double[,] Ke = Calcul_Ke_Beam3D(elem, noeud);
            double[] qe = qe_Beam3D(elem, noeud,q);
            double[] Fe=new double[12];

            double u = 0;
            for (int i = 0; i < 12; i++)
            {
                u = 0;
                for (int j = 0; j < 12; j++)
                    u += Ke[i, j] * qe[j];
                Fe[i] = u;
            }

            return Fe;
        }


        static public double[] Rotation_qe_Beam3D(DataRow elem, List<double[]> noeud, double[] qeG)
        {
            double[] qeL = new double[12];

            double[,] R = Matrice_R_Beam3D(elem, noeud);

            int i, j;
            double u = 0;
            for (i = 0; i < 12; i++)
            {
                u = 0;
                for (j = 0; j < 12; j++)
                    u += R[i,j] * qeG[j];
                qeL[i] = u;
            }
            return qeL;
        }


    }
}
