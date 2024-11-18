using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using System.Data;
using MathNet.Numerics.LinearAlgebra.Double;
using AliantWebS.Models;
using System.Xml.Linq;
using Accord.Math;
using System.Runtime.InteropServices;

namespace Structural
{
    static public class StructuralTimeIntegration
    {

        static public double[,] Calcul_Dynamique(int duree, int nbReponse, int nbIntegration,
             int nddl, List<double[]> noeud,  DataTable element, List<string[]> CL,
        DataTable MasseNodale,
                       List<(Guid id, List<int> noeudInd, double value, double position)> ChargesStatiquePonctuelles,
                       List<(Guid id, List<int> noeudInd, double startValue, double endValue)> ChargesStatiqueReparties,
                       List<(Guid id, List<int> noeudInd, double value, double position, Dictionary<double, double> tempFunc)> ChargesDynamiquePonctuelles,
                       List<(Guid id, List<int> noeudInd, double startValue, double endValue, Dictionary<double, double> tempFunc)> ChargesDynamiqueReparties,
                       List<(Guid id, List<int> noeudInd, double ValueVL, double ValuePL, double ValueConvoi, double SpeedVL,
            double SpeedPL, double AverageHourlyTraffic, double PercentagePL, double LoadUncertainty, double SpeedUncertainty)> ChargesMobiles )
        {
            string LoadType = "Static";
            if (ChargesDynamiquePonctuelles.Count != 0 || ChargesDynamiqueReparties.Count != 0) LoadType = "Dynamic";
            if (ChargesMobiles.Count != 0 ) LoadType = "Traffic";

            int nbTime = nbReponse * duree;
            int ratioReponse = nbIntegration / nbReponse;
            double dt = 1.0 / nbIntegration;

            double [,] qTime = new double[nbTime, nddl];
            double[,] K = StructuralVibration.Calcul_K(element, noeud);
            double[,] M = StructuralVibration.Calcul_M(element, noeud);
            M = StructuralVibration.Calcul_MNoeud(MasseNodale, M);

            double beta = 0.5, gamma = 0.5;

            double[] Fn = new double[nddl];
            double[] Fn1 = new double[nddl];

            double[] qn = new double[3 * noeud.Count()];
            double[] qn1 = new double[3 * noeud.Count()];
            double[] vn = new double[3 * noeud.Count()];
            double[] vn1 = new double[3 * noeud.Count()];
            double[] an = new double[3 * noeud.Count()];
            double[] an1 = new double[3 * noeud.Count()];

            double[,] Kt = new double[nddl, nddl];
            double[,] C = new double[nddl, nddl];
            int i, j;
            for (i = 0; i < nddl; i++) Fn[i] = Fn1[i] = 0;
            for (i = 0; i < nddl; i++)
                for (j = 0; j < nddl; j++)
                {
                    if (i == j && Math.Abs(M[i, j] - 1) < 1e-5)
                        C[i, j] = 0;
                    else
                        C[i, j] = 0.00 * M[i, j] + 0.002 * K[i, j];

                }

            for (i = 0; i < nddl; i++)
                for (j = 0; j < nddl; j++)
                    Kt[i, j] = M[i, j] / (beta * dt * dt) + C[i, j] * gamma / (beta * dt) + K[i, j];
            DenseMatrix KT = DenseMatrix.OfArray(Kt);

            MathNet.Numerics.LinearAlgebra.Matrix<double> Ks = DenseMatrix.OfArray(K);
           
            //*** Conditions aux limites ***
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
                    for (i = 0; i < 3 * noeud.Count(); i++)
                    {
                        KT[i, 3 * inoeud + iddl] = KT[3 * inoeud + iddl, i] = 0;
                        Ks[i, 3 * inoeud + iddl] = Ks[3 * inoeud + iddl, i] = 0;
                        K[i, 3 * inoeud + iddl] = K[3 * inoeud + iddl, i] = 0;
                        M[i, 3 * inoeud + iddl] = M[3 * inoeud + iddl, i] = 0;
                        C[i, 3 * inoeud + iddl] = C[3 * inoeud + iddl, i] = 0;
                    }
                    KT[3 * inoeud + iddl, 3 * inoeud + iddl] = 1;
                    Ks[3 * inoeud + iddl, 3 * inoeud + iddl] = 1;
                    K[3 * inoeud + iddl, 3 * inoeud + iddl] = M[3 * inoeud + iddl, 3 * inoeud + iddl] = C[3 * inoeud + iddl, 3 * inoeud + iddl] = 1;
                }
            }


            MathNet.Numerics.LinearAlgebra.Matrix<double> K1 = KT.Inverse();

            for (i = 0; i < nddl; i++) Fn[i] = qn[i] = vn[i] = an[i] = 0;

            //*** Condition initiale : Calcul statique ***
            foreach (var charge in ChargesDynamiquePonctuelles)
            {
                double P = 1000*charge.value* charge.tempFunc.ElementAt(0).Value;
                for (j = 0; j < charge.noeudInd.Count; j++)
                    Fn[3 * charge.noeudInd[j] + 1] += P;
            }

            Ks = Ks.Inverse();
            for (i = 0; i < nddl; i++)
            {
                double s = 0;
                for (j = 0; j < nddl; j++)
                    s += Ks[i, j] * Fn[j];
                qn[i] = s;
            }

            //***************************
            // Boucle principale
            //***************************
            double t1, t2;
            for (int iTemps = 1; iTemps < nbIntegration * duree; iTemps++)
            {
                for (i = 0; i < nddl; i++) Fn1[i] = 0;
                /* rafic switch (LoadType)
                  {
                      case "Traffic1dir":

                          foreach (double[] vcle in Vehicle)
                          {
                              Poids = vcle[0];
                              Vitesse = vcle[1];
                              ti0 = vcle[2];

                              xP = Vitesse * (iTemps * dt - ti0);
                              foreach (DataRow el in element.Rows)
                              {
                                  int ni = el.Field<int>("Noeudi");
                                  int nj = el.Field<int>("Noeudj");
                                  if (Math.Abs(noeud[ni][1]) > 0.001 || Math.Abs(noeud[nj][1]) > 0.001) continue;
                                  double xi = noeud[ni][0];
                                  double xj = noeud[nj][0];
                                  if (xP > xi && xP < xj)
                                  {
                                      double dx = xj - xi;
                                      i = (int)(xP / dx);
                                      double dxP = xP - i * dx;
                                      Fn1[3 * ni + 1] = Fn1[3*ni+1] + Poids * (dx - dxP) / dx;
                                      Fn1[3 * nj + 1] = Fn1[3 * nj + 1] + Poids * (dxP) / dx;

                                  }
                              }
                          }
                          break;
                      case "Creneau":
                          Poids = Vehicle[0][0];
                          double dureeCharge = Vehicle[0][1];
                          ti0 = Vehicle[0][2];

                       //   if (iTemps * dt >= ti0 && iTemps * dt <= ti0 + dureeCharge)
                      //        Fn1[iqt] = Poids;

                          break;
                      case "Lacher":
                          //Poids = Vehicle[0][0];
                          //ti0 = Vehicle[0][2];

                          //    if (iTemps * dt <= ti0 )
                          //       Fn1[iqt] = 0;
                          //   if(iTemps==1)  Fn1[iqt] = -1000;
                          if (iTemps < nbIntegration * 0.0)
                              Fn1[3 * ncharge+1 ] = (-200000.0) ;
  //                        Fn1[3 * ncharge + 1] = (-29.05 / 5) * dx;
                          else
                              Fn1[3 * ncharge+1 ] = 0.00;
                          //                            Fn1[3 * ncharge + 1] = 0.00;

                          break;
                  }*/
                
                
                //*** Application des charges ***

                foreach (var charge in ChargesDynamiquePonctuelles)
                {
                    double P = 0;
                    for (i = 0; i < charge.tempFunc.Count - 1; i++)
                    {
                        t1 = charge.tempFunc.ElementAt(i).Key;
                        t2 = charge.tempFunc.ElementAt(i + 1).Key;
                        P = 0;
                        if (t1 <= iTemps * dt && iTemps * dt < t2)
                        {
                            double deltaTemps = t2 - t1;
                            P = 1000 * charge.value
                                * (charge.tempFunc.ElementAt(i).Value * (t2 - iTemps * dt) / deltaTemps
                                + charge.tempFunc.ElementAt(i + 1).Value * (iTemps * dt - t1) / deltaTemps);
                        }
                    }

                    t2 = charge.tempFunc.ElementAt(charge.tempFunc.Count - 1).Key;
                    if (iTemps * dt > t2)
                    {
                        P = 1000 * charge.value * charge.tempFunc.ElementAt(charge.tempFunc.Count - 1).Value;
                        for (j = 0; j < charge.noeudInd.Count; j++)
                        {
                            Fn1[3 * charge.noeudInd[j] + 1] += P;
                        }
                    }
                        

                    
                }


                for (i = 0; i < nddl; i++)
                {
                    double s = Fn1[i];
                    for (j = 0; j < nddl; j++)
                        s += -K[i, j] * (qn[j] / (beta * dt * dt) + vn[j] / (beta * dt) + (1 / (2 * beta) - 1) * an[j])
                           - C[i, j] * ((1 / (beta * dt * dt)) * vn[j] + (1 - gamma) / (beta * dt) * an[j]);
                    Fn1[i] = s;
                }

                //*** Conditions aux limites sur les charges *** 
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
                        Fn1[3 * inoeud + iddl] = 0;
                    }
                }

                //*** Calcul des accélérations, puis vitesse et déplacement ***
                for (i = 0; i < nddl; i++)
                {
                    double s = 0;
                    for (j = 0; j < nddl; j++)
                        s += K1[i, j] * Fn1[j];

                    an1[i] = s;
                }

                for (j = 0; j < nddl; j++)
                {
                    vn1[j] = vn[j] + (1 - gamma) * dt * an[j] + gamma * dt * an1[j];
                    qn1[j] = qn[j] + dt * vn[j] + dt * dt * ((0.5 - beta) * an[j] + beta * an1[j]);
                }

                //*** Mise à jour pour les incréments suivants ***
                for (j = 0; j < nddl; j++)
                {
                    vn[j] = vn1[j];
                    qn[j] = qn1[j];
                    an[j] = an1[j];
                    Fn[j] = Fn1[j];
                }

                //*** Remplissage de qTime ***
                int ik = 0;
                Math.DivRem(iTemps, ratioReponse, out ik);
                if (ik == 0)
                {
                    int ikk = iTemps / ratioReponse;
                    for (j = 0; j < nddl; j++)
                        qTime[ikk, j] = an1[j] / 10;
                }

            }


            return qTime;
        }
    }
}
