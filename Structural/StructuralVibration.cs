using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Data;
using MathNet.Numerics.LinearAlgebra;
using MathNet.Numerics.LinearAlgebra.Double;
using MathNet.Numerics.LinearAlgebra.Double.Factorization;

namespace Structural
{
    public static class StructuralVibration
    {
  




        static public double[] Calcul_ValeursPropres(DataTable element, List<double[]> noeud, DataTable MasseNodale, List<string[]> CL, out double [,] vectPropres)
        {


            double[,] K = Calcul_K(element, noeud);
            double[,] M = Calcul_M(element, noeud);
            M = StructuralVibration.Calcul_MNoeud(MasseNodale, M);

            int ndll = 3 * noeud.Count();

            double[] Vp = new double[3 * noeud.Count()];
            DenseMatrix KK = DenseMatrix.OfArray(K);
            DenseMatrix MM = DenseMatrix.OfArray(M);


            int inoeud, iddl = 0;

            foreach (string[] cl in CL)
            {
                inoeud = iddl = 0;
                if (int.TryParse(cl[0], out inoeud))
                {
                    if (inoeud > noeud.Count - 1) continue;
                    switch (cl[1].ToUpper())
                    {
                        case "U":
                            iddl = 0; break;
                        case "V":
                            iddl = 1; break;
                        case "R":
                            iddl = 2; break;
                        default:
                            iddl = 0; break;
                    }
                    for (int i = 0; i < 3 * noeud.Count(); i++)
                        KK[i, 3 * inoeud + iddl] = KK[3 * inoeud + iddl, i] =
                        MM[i, 3 * inoeud + iddl] = MM[3 * inoeud + iddl, i] = 0;
                    KK[3 * inoeud + iddl, 3 * inoeud + iddl] = MM[3 * inoeud + iddl, 3 * inoeud + iddl] = 1;
                }
            }

            double d = MM.Determinant();
            if (Math.Abs(d) < 1e-6)
                d = KK.Determinant();
            //return null;

            MathNet.Numerics.LinearAlgebra.Matrix<double> M1 = MM.Inverse();
            MathNet.Numerics.LinearAlgebra.Matrix<double> M1K = M1 * KK;




            foreach (string[] cl1 in CL)
            {
                inoeud = iddl = 0;

                if (int.TryParse(cl1[0], out inoeud))
                {
                    if (inoeud > noeud.Count - 1) continue;
                    switch (cl1[1].ToUpper())
                    {
                        case "U":
                            iddl = 0; break;
                        case "V":
                            iddl = 1; break;
                        case "R":
                            iddl = 2; break;
                        default:
                            iddl = 0; break;
                    }
                    for (int i = 0; i < 3 * noeud.Count(); i++)
                        M1K[3 * inoeud + iddl, i] = M1K[i, 3 * inoeud + iddl] = 0;
                    //               K1M[3 * inoeud + iddl, 3 * inoeud + iddl] = 1;
                }
            }


            /*
             // Calcul des valeurs propres par la méthode de Jacobi ... mais la subroutine ne fonctionne pas bien.. donc à vérifier !!
            double[] Val;
            double[,] v ;
            double[,] A = new double[ndll, ndll];
            for (int i = 0; i < ndll; i++) for (int j = 0; j < ndll; j++) A[i, j] = K1M[i, j];

            eigenJacobi(A, ndll,out Val,out v);
            eigenSort(Val,v,nddl);
            for (int i = 0; i < ndll; i++)
                if (Math.Abs(Val[i]) > 1e-20) Val[i] = 1 / Val[i];
                else
                    Val[i] = 1e20;
                    */
            vectPropres = new double[3 * noeud.Count(), 3 * noeud.Count()];
            MathNet.Numerics.LinearAlgebra.Factorization.Evd<double> eigen = M1K.Evd();
            MathNet.Numerics.LinearAlgebra.Vector<System.Numerics.Complex> eigenvalues = eigen.EigenValues;
            for (int i = 0; i < 3 * noeud.Count(); i++)
                Vp[i] = Math.Sqrt(eigenvalues[i].Real);

            MathNet.Numerics.LinearAlgebra.Matrix<double> eigenvector = eigen.EigenVectors;
            for (int i = 0; i < 3 * noeud.Count(); i++)
                for (int j = 0; j < 3 * noeud.Count(); j++)
                    vectPropres[i, j] = eigenvector[i, j];

            vectorSort(Vp, vectPropres);
            return Vp;
        }
        static private double[,] Calcul_Ke(DataRow elem, List<double[]> noeud)
        {
            double xi = noeud[elem.Field<int>("Noeudi")][0];
            double yi = noeud[elem.Field<int>("Noeudi")][1];
            double xj = noeud[elem.Field<int>("Noeudj")][0];
            double yj = noeud[elem.Field<int>("Noeudj")][1];
            double E = elem.Field<double>("E");
            double S = elem.Field<double>("S");
            double I = elem.Field<double>("Iy");

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
            foreach  (DataRow elem in element.Rows)
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
                M[3 *n + 0, 3 * n + 0] += m;
                M[3 * n + 1, 3 * n + 1] += m;
            }
            return M;
        }


        static private void Assemblage(double[,] A,double[,] Ag, DataRow elem)
        {
            int i, j, ik, jk;
            int[] n = new int[2];
            n[0] = elem.Field<int>("Noeudi");
            n[1]= elem.Field<int>("Noeudj");
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

      static  public double[,] Matrice_R(DataRow elem, List<double[]> noeud)
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


    static    public double[] Rotation_qe(DataRow elem, List<double[]> noeud, double[] qeG)
        {
            double[] qeL = new double[ 6];

            double[,] R = Matrice_R(elem, noeud);

            int i, j;
            double u = 0;
            for (i = 0; i < 6; i++)
            {
                u = 0;
                for (j = 0; j < 6; j++)
                     u += R[ j,i] * qeG[ j];
                    qeL[i] = u;
                }
            return qeL;
        }


        static private void eigenSquareMatrixLargestEigenValues(double[,] a, int n)
        {
            int i, j, flag;
            double[] x0 = new double[10];
            double[] x1 = new double[10];
            double[] y = new double[10];
            double lambda, eps = 1e-5;
            for (i = 1; i <= n; i++)
            {
                x0[i] = 1; x1[i] = 1;
            }
            do
            {
                flag = 0;
                for (i = 1; i <= n; i++) x0[i] = x1[i];
                for (i = 1; i <= n; i++)
                {
                    y[i] = 0;
                    for (j = 1; j <= n; j++) y[i] += a[i, j] * x0[j];
                }
                lambda = y[1];
                for (i = 2; i <= n; i++) if (lambda < y[i]) lambda = y[i];
                for (i = 1; i <= n; i++) x1[i] = y[i] / lambda;
                for (i = 1; i <= n; i++) if (Math.Abs(x0[i] - x1[i]) > eps) flag = 1;
            }
            while (flag == 1);

            //printf(”The largest eigenvalue is %8.5f ”, lambda);
            //printf(”The corresponding eigenvector is ”);
            //for(i = 1;i <= n;i + +) printf(”%8.5f ”, x1[i]);
        }

        static private void eigenJacobi(double[,] a, int n,out double[] d,out double [,] v)
        {


            //d[1..n] returns the eigenvalues of a.
            //v[1..n][1..n] is a matrix whose columns contain, on output, the normalized eigenvectors of a.
            //nrot returns the number of Jacobi rotations that were required.
            {
                int j, iq, ip, i;
                double tresh, theta, tau, t, sm, s, h, g, c;
                double[] b = new double[n];
                double[] z = new double[n];
                d = new double[n];
                v = new double[n, n];
                int nrot ;

                    //   Initialize to the identity matrix.
                for (ip = 0; ip < n; ip++)
                {
                    for (iq = 0; iq < n; iq++) v[ip, iq] = 0.0;
                    v[ip, ip] = 1.0;
                }

                for (ip = 0; ip < n; ip++)
                {
                    // Initialize b and d to the diagonal
                    b[ip] = d[ip] = a[ip, ip];
                    z[ip] = 0.0;
                }
                nrot = 0;
                for (i = 0; i < 50; i++)
                {
                    sm = 0.0;
                    for (ip = 0; ip < n - 1; ip++)
                    {

                        for (iq = ip + 1; iq < n; iq++)
                            sm += Math.Abs(a[ip, iq]);
                    }
                    if (sm == 0.0)
                    {
                        //  free_vector(z, 1, n);
                        //   free_vector(b, 1, n);
                        return;
                    }
                    if (i < 3)
                        tresh = 0.2 * sm / (n * n);
                    else
                        tresh = 0.0;
                    for (ip = 0; ip < n - 1; ip++)
                    {
                        for (iq = ip + 1; iq < n; iq++)
                        {
                            g = 100.0 * Math.Abs(a[ip, iq]);
                            //After four sweeps, skip the rotation if the off - diagonal element is small.
                            if (i > 3 
                                && (Math.Abs(d[ip]) + g) == Math.Abs(d[ip])
                                && (Math.Abs(d[iq]) + g) == Math.Abs(d[iq]))
                                a[ip, iq] = 0.0;
                            else if (Math.Abs(a[ip, iq]) > tresh)
                            {
                                h = d[iq] - d[ip];
                                if ((Math.Abs(h) + g) == Math.Abs(h))
                                    t = (a[ip, iq]) / h;
                                else
                                {
                                    theta = 0.5 * h / (a[ip, iq]);
                                    t = 1.0 / (Math.Abs(theta) + Math.Sqrt(1.0 + theta * theta));
                                    if (theta < 0.0) t = -t;
                                }
                                c = 1.0 / Math.Sqrt(1 + t * t);
                                s = t * c;
                                tau = s / (1.0 + c);
                                h = t * a[ip, iq];
                                z[ip] -= h;
                                z[iq] += h;
                                d[ip] -= h;
                                d[iq] += h;
                                a[ip, iq] = 0.0;
                                for (j = 0; j < ip - 1; j++)
                                {
                                    //                    Case of rotations 1 ≤ j < p.
                                    ROTATE(a, j, ip, j, iq, s, tau);
                                }
                                for (j = ip; j < iq - 1; j++)
                                {
                                    //                  Case of rotations p< j < q.
                                    ROTATE(a, ip, j, j, iq, s, tau);
                                }
                                for (j = iq; j < n; j++)
                                {
                                    //                Case of rotations q< j ≤ n.
                                    ROTATE(a, ip, j, iq, j, s, tau);
                                }
                                for (j = 0; j < n; j++)
                                {
                                    ROTATE(v, j, ip, j, iq, s, tau);
                                }
                                ++nrot;
                            }
                        }
                    }
                    for (ip = 0; ip < n; ip++)
                    {
                        b[ip] += z[ip];
                        d[ip] = b[ip];
                        z[ip] = 0.0;
                    }
                }
                //nrerror("Too many iterations in routine jacobi");
            }
        }
        static private void ROTATE(double[,] a, int i, int j, int k, int l,double s, double tau)
        {
            double g = a[i, j];
            double h = a[k, l];
            a[i, j] = g - s * (h + g * tau);
            a[k, l] = h + s * (g - h * tau);
        }

        static private void eigenSort(double[] d, double[,] v, int n)
        {
            int k, j, i;
            double p;
            for (i = 0; i < n-1; i++)
            {
                p = d[k = i];
                for (j = i + 1; j < n; j++)
                    if (d[j] >= p) p = d[k = j];
                if (k != i)
                {
                    d[k] = d[i];
                    d[i] = p;
                    for (j = 0; j < n; j++)
                    {
                        p = v[j, i];
                        v[j, i] = v[j, k];
                        v[j, k] = p;
                    }
                }
            }
        }

        static private double[,] vectorSort(double[] d, double[,] vect)
        {
            int k, j, i,n=d.Length;
            double p,v;
            for (i = 0; i < n-1; i++)
            {
                p = d[k = i];
                for (j = i + 1; j < n; j++)
                    if (d[j] <= p) p = d[k = j];
                if (k != i)
                {
                    d[k] = d[i];
                    d[i] = p;
                    for (int r = 0; r< n ; r++)
                    {
                        v = vect[r, k];
                        vect[r,k] = vect[r, i];
                        vect[ r, i] = v;
                    }
                }
            }
            return vect;
        }

    }
}
