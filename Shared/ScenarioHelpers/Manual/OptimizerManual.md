# Manuel de l'optimiseur d'implantation

Ce document explique comment l'application calcule une implantation de rigging : ce qu'elle place, avec
quelles règles, quelles tolérances, et ce qu'elle refuse de faire. Il s'adresse à qui doit pouvoir défendre
un plan devant un ingénieur ou le reprendre dans le code — il ne suppose aucune connaissance préalable de
l'un ni de l'autre.

Chaque chapitre part d'un problème concret, montre pourquoi la réponse évidente ne tient pas, puis donne la
règle que le code applique, avec ses chiffres et un exemple qu'on peut refaire à la main. Les sections se
terminent par le fichier et la méthode à ouvrir : le manuel est fait pour être lu à côté du code, pas à sa
place.

Les exemples chiffrés viennent de deux jeux de fichiers réels, et le texte dit lequel à chaque fois : les
classeurs d'exemple livrés avec l'application (`wwwroot/exemples` : 8 charges, 2 516 kg) et le plan de la
salle avec son inventaire, dans le dossier partagé `_common/data` (24 charges, 6 210 kg). Les résultats
complets cités — 6 palonniers, 7 lignes de mother, 93,438 m de pont — ont été obtenus en faisant tourner
l'optimiseur sur ces fichiers, pas déduits à la main. Un autre inventaire donne d'autres chiffres, jamais
d'autres règles.

> Ce texte est embarqué dans l'application : la page qui l'affiche et le fichier Word qu'elle exporte
> impriment le même document. Il décrit le code en service au moment de l'édition — si une règle vous
> surprend, le fichier nommé en fin de section fait foi.

## Chapitre 1 — Le problème que résout l'optimiseur

### 1.1 Ce qu'on lui demande

Une production arrive avec un plan. Ce plan dit : un moteur ici, de tant de kilos ; un autre là. Les positions sont celles du spectacle — la place des projecteurs, des enceintes, du décor. Elles ne doivent rien à la salle.

La salle, elle, n'offre pas de points là où la production les veut. À l'Antarès, les charges ne peuvent être reprises qu'aux **points d'accroche** définis sur les poutres bois : une grille fixe, environ 7,2 m d'une poutre à la suivante, environ 3,7 m d'un point au suivant sur une même poutre. Entre deux points, il n'y a rien. Un moteur posé à 1,80 m d'un point d'accroche n'a, à sa verticale, rien à quoi pendre.

La réponse naïve serait : déplacer le moteur au point le plus proche. Elle est fausse dès la première charge, parce que le plan de charges est celui de la production : le déplacer, c'est changer le spectacle. La deuxième réponse naïve serait : tirer une élingue en biais jusqu'au point le plus proche. Elle est fausse aussi : une élingue oblique introduit une composante horizontale que la charpente ne reprend pas, et un seul point d'accroche n'a pas la capacité pour tout.

On interpose donc de la structure, et toujours la même, celle que les études manuscrites décrivent :

```
charge (moteur)
   └─ palonnier            pont le long des poutres, d'une ligne de points à la suivante
        └─ ligne de mother pont en travers des poutres, au-dessus d'une ligne de points
             └─ point d'accroche          →  poutre bois
```

L'optimiseur est la pièce de code qui construit ces deux couches. On lui donne le plan de charges et l'inventaire des ponts disponibles ; il rend une implantation : où sont les lignes de mother, où sont les palonniers, de quelles pièces chacun est assemblé, quelle charge pend de quoi, et à quelles lignes chaque palonnier est accroché.

Le point d'entrée est `Pipeline.GenerateLayout`. Le bouton « Générer l'implantation » de la page de conception l'appelle, et l'enchaînement est court :

| Étape | Ce qui se passe | Où |
|---|---|---|
| 1 | Lecture de l'inventaire seul : `MotherGrill.xlsx` et `Palonnier.xlsx` | `ExcelLoader.LoadInventory` |
| 2 | Création des `ElementType` manquants dans la catégorie `mothergridpalonnier` | `LayoutPersistence.EnsureElementTypes` |
| 3 | Lecture du plan de charges dans les lignes `ChargesScenique` du scénario, tri, contrôles | `PlanSource.Read` |
| 3b | Lecture des réglages : classement des critères, niveau de recherche, orientations orthogonales | `OptimizerSettings.ReadAsync` |
| 4 | Placement | `Optimizer.Optimize` |
| 4b | Remplacement des éléments et des `ChargeMp` du scénario, en une transaction | `LayoutPersistence.ReplaceElementsAsync` |
| 5 | Plan PNG `rigging_plan_v5.png` | `LayoutArtifacts.Write` → `PlanDrawer.Draw` |
| 6 | Rapport Excel `rigging_report_v5.xlsx` | `LayoutArtifacts.Write` → `ExcelExporter.Export` |

`Pipeline.cs` ne contient plus que cet enchaînement : 131 lignes, aucune règle. Chaque étape a son fichier — le plan dans `PlanSource.cs`, les réglages dans `OptimizerSettings.cs`, la base dans `LayoutPersistence.cs`, le plan PNG et le classeur dans `LayoutArtifacts.cs`, les messages de refus dans `Pipeline.Messages.cs`, les lignes à écrire dans `Pipeline.Entities.cs`, les imports Excel dans `Pipeline.Imports.cs`. Le fichier se lit donc d'une traite, et la carte du chapitre 14 dit ce que chacun porte.

« Résoudre » ne veut pas dire ici trouver le meilleur plan possible. Cela veut dire produire un plan qu'un rigger reconnaîtrait, que l'étude de charges peut ensuite vérifier ligne par ligne, et dont chaque pièce existe vraiment dans le stock. Le § 1.3 dit comment il s'y prend ; le **chapitre 15** dit ce qu'il cherche à rendre petit, dans quel ordre et qui en décide ; le **chapitre 16** dit ce qu'on sait prouver de la réponse.

**À lire :** `Shared/ScenarioHelpers/Pipeline.cs`, `GenerateLayout`, et les fichiers qu'il appelle (`PlanSource.cs`, `OptimizerSettings.cs`, `LayoutPersistence.cs`, `LayoutArtifacts.cs`) ; `AliantWebS/Pages/AntaresConceptionLayout.razor`, `GenerateLayout`.

### 1.2 Ce qu'il ne doit jamais faire

Un placement qui se trompe à moitié est pire qu'un placement qui refuse. L'enregistrement **remplace** les éléments du scénario : une implantation incomplète écraserait une implantation juste. D'où une liste de refus : les quatre que `GenerateLayout` construit lui-même — plan vide, plan non plaçable, charges non portées, déséquilibre — finissent tous par la même phrase, « L'implantation existante n'a pas été modifiée. » ; les refus venant de la lecture des fichiers d'entrée, eux, ne la portent pas.

- **Ne jamais enregistrer une implantation qui laisse une charge sans support.** Si `result.Uncovered` n'est pas vide, `GenerateLayout` lève avant d'écrire quoi que ce soit.
- **Ne jamais enregistrer une implantation incohérente.** Après placement, la somme des charges scéniques ramenées aux points d'accroche est comparée à la somme des charges portées ; l'écart admis est de **2,0 kg** (`Optimizer.cs`, `balanceOk = Math.Abs(totalGrid - carriedTotal) < 2.0`). Sur le fichier d'exemple, qui totalise 2 516 kg, cela représente 0,08 %. Ce n'est pas une tolérance de calcul : c'est un garde-fou contre une contradiction interne, typiquement deux charges portant le même nom.
- **Ne jamais remplacer à moitié.** La suppression des anciens éléments et l'insertion des nouveaux se font dans une transaction unique, rejouée en entier en cas d'échec transitoire.
- **Ne jamais utiliser plus de pièces que l'inventaire n'en contient.** Tout passe par `PieceStock` : une pièce prise n'est plus disponible pour un autre pont, et une pièce rendue (palonnier abandonné) revient au stock.
- **Ne jamais dépasser la valeur admissible d'une travée en accrochant un palonnier de plus.** Un croisement qui ferait dépasser est laissé libre, et la raison est écrite dans l'élément (`Accrochages`).
- **Ne jamais lire `Charges.xlsx` au placement.** Le plan, ce sont les lignes de la page Charges. `Charges.xlsx` n'est qu'un moyen de remplir cette page.
- **Ne jamais convertir les coordonnées une deuxième fois.** Voir § 2.2.

Les quatre premiers de cette liste sont désormais écrits noir sur blanc dans le code, sous le nom de **contraintes dures**, chacun à côté de la méthode qui l'applique : `Objective.HardConstraints`. Ce ne sont pas des critères, et ils n'ont aucun poids — une implantation qui en casse un n'est pas une implantation moins bonne, ce n'est pas une implantation. Le chapitre 15 les reprend une par une.

Et voici ce qu'un lecteur suppose à tort :

> L'optimiseur ne vérifie **pas** le cahier des charges de la salle. Il calcule bien la charge à chaque point d'accroche (`gridLoads`), mais ne la compare à aucune capacité de point ni de poutre. Cette vérification appartient au Calcul Charpente et à l'étude. De même, les erreurs de capacité relevées à la fin de `Optimize` (`capErrors` : un palonnier ou une travée au-delà de sa valeur) sont écrites dans la console et dans `result.Errors`, mais **n'empêchent pas l'enregistrement** : seuls `Uncovered` et l'équilibre le font.

Deuxième piège : `gridLoads` ne contient que les charges **scéniques**. Dans `Mother.Compute`, seule la composante `F` d'un point de suspension est répartie sur les poutres ; la part de poids propre des palonniers, `W`, ne l'est pas. Ce poids propre est bien compté pour dimensionner les travées (`BayLoadsOf` additionne `F + W`), mais il ne ressort pas dans les charges aux points de grille de l'optimiseur. C'est l'étude qui transmet les masses de ponts aux points d'accroche.

**À lire :** `Shared/ScenarioHelpers/Pipeline.cs`, `GenerateLayout` ; `Shared/ScenarioHelpers/LayoutPersistence.cs`, `ReplaceElementsAsync` ; `Shared/ScenarioHelpers/Core/Optimizer.cs`, `Optimize` ; `Shared/ScenarioHelpers/Core/Objective.cs`, `HardConstraints` ; `Shared/ScenarioHelpers/Models/Models.cs`, `Mother.Compute` et `Mother.BayLoadsOf`.

### 1.3 Les trois questions auxquelles il répond

Tout le placement tient dans trois questions, dans cet ordre.

#### 1. Cette charge a-t-elle besoin d'un palonnier ?

Une charge qui tombe sur une ligne de points n'a besoin de rien d'autre : elle pend directement de la ligne de mother, comme dans les études manuscrites. Le critère est purement géométrique : la ligne de points la plus proche en y, et l'écart comparé à `Geometry.TOL` = **0,08 m**.

Sur le fichier d'exemple livré avec l'application (`AliantWebS/wwwroot/exemples/Charges.xlsx`, huit charges, 2 516 kg au total) :

| Charge | X, Y étude (m) | X, Y grille (m) | Ligne la plus proche | Écart | Verdict |
|---|---|---|---|---|---|
| L1 | −7,45 ; −2,99 | 2,99 ; 7,45 | ligne 3 (y = 7,400) | 0,05 m | directe |
| L2 | 0 ; −2,99 | 2,99 ; 0 | ligne 5 (y = 0) | 0,00 m | directe |
| L3 | 7,40 ; −2,99 | 2,99 ; −7,40 | ligne 7 (y = −7,450) | 0,05 m | directe |
| L4 | −7,45 ; 1 | −1 ; 7,45 | ligne 3 | 0,05 m | directe |
| L5 | 0 ; 1 | −1 ; 0 | ligne 5 | 0,00 m | directe |
| L6 | 7,40 ; 1 | −1 ; −7,40 | ligne 7 | 0,05 m | directe |
| S1 | −9 ; −1 | 1 ; 9 | ligne 3 | 1,60 m | palonnier |
| S2 | 9 ; −1 | 1 ; −9 | ligne 7 | 1,55 m | palonnier |

Six charges sur huit ne demandent aucun palonnier. C'est le cas courant, et c'est voulu : les études accrochent la plupart des moteurs directement au mother.

#### 2. Quels palonniers, faits de quelles pièces, portant quelles charges ?

Pour les charges restantes, on énumère tous les **groupes candidats** : les charges qui partagent un même x (à `TOL` près), portées par un palonnier tendu de la ligne de points en dessous à celle au-dessus ; et, si le réglage l'autorise, les groupes de 2 à 4 charges alignées en biais. Puis une sélection **gloutonne** : le plus grand nombre de charges d'abord, un biais départage à égalité, la plus petite portée ensuite. Cette préférence tient en une comparaison, `Optimizer.PreferGroup`, et ne repose plus sur aucun nombre ajusté à la main (§ 7.4). À chaque choix, les pièces sont retirées du stock, de sorte que le choix suivant ne voit que ce qui reste.

Reprenons S1. Sa position en grille est (x = 1 ; y = 9). Les lignes qui l'encadrent sont la ligne 3 (y = 7,400) et la ligne 2 (y = 11,050) : le palonnier doit donc franchir 11,050 − 7,400 = **3,650 m**, en portant 550 kg. L'assemblage cherché doit mesurer au moins 3,650 − 0,08 = **3,570 m** : aucune pièce de 3 m de l'inventaire d'exemple n'y suffit seule, il faudra en joindre deux.

#### 3. Quelles lignes de mother, et quels croisements accrochés ?

Chaque ligne de points qui a quelque chose à porter — une extrémité de palonnier, ou une charge directe — reçoit une ligne de mother continue, sur la suite des travées concernées, appuyée sur chaque poutre traversée. Chaque travée est dimensionnée séparément. Puis chaque palonnier est accroché à **toutes** les lignes qu'il croise entre ses deux extrémités, sauf celles où l'accrochage ferait dépasser une travée.

#### Pourquoi le placement est glouton, et où l'optimum est quand même cherché

Le nombre de groupes candidats explose. Pour un plan de 24 charges, les seuls groupes biais de taille 2 à 4 sont déjà C(24,2) + C(24,3) + C(24,4) = 276 + 2 024 + 10 626 = **12 926** combinaisons à filtrer, avant même les groupes en Y, qui sont en 2^k − 1 par paquet de k charges de même x. Choisir le meilleur sous-ensemble de groupes qui couvre toutes les charges est un problème de couverture : on ne le résout pas à chaque clic.

Le code assume ce choix. Il est glouton, donc **l'ordre de lecture du plan change le résultat**. C'est pour cela que l'ordre est figé dans `PlanSource.Read` : `Numero` d'abord quand il est renseigné, puis le nom trié comme une personne numérote ses charges (T2 avant T10, `NaturalStringComparer`). Deux générations successives sur le même plan donnent la même implantation ; une charge renommée peut en donner une autre.

Cela ne veut plus dire que rien n'est su de la qualité de la réponse. Deux choses l'accompagnent aujourd'hui, et le chapitre 16 leur est consacré :

- **Un plancher**, calculé à chaque génération : le minimum de palonniers, de pièces et de mètres de mother qu'aucune implantation de ce plan ne peut passer. Il ne dit jamais qu'une meilleure implantation existe ; il dit qu'on ne peut pas faire moins.
- **Une recherche exacte**, sur demande (réglage `optimizer.searchLevel`, § 11.3) : elle parcourt toutes les façons de grouper les charges qui ont besoin d'un palonnier, part de l'implantation gloutonne et ne la remplace que par une que le classement préfère. Quand elle a tout parcouru, elle le dit — « optimum prouvé », avec ce que cette phrase couvre exactement et rien de plus.

Le critère de réussite n'est donc pas « le moins d'acier possible » : ce sont d'abord quatre contraintes dures — toutes les charges portées, aucune pièce inventée, chaque travée dans sa valeur, le réglage orthogonal respecté —, ensuite seulement le classement de critères du chapitre 15, et par-dessus tout un plan que l'étude peut reprendre telle quelle. C'est d'ailleurs la structure de l'étude produite : PRELIMINAIRES (objet, documents de référence), PRESENTATION DE LA SALLE (système d'accroche, nomination des éléments, cahier des charges), IMPLANTATION DANS LA SALLE (plan de charges de la production, implantation — « Mother + pré rigg » puis « Production » —, résultats de l'étude de charges : Mother, Pré rigg, Points d'accroche de la salle), CONCLUSION.

**À lire :** `Shared/ScenarioHelpers/Core/Optimizer.cs`, le commentaire de tête puis `Optimize` et `SelectGreedy` ; `Shared/ScenarioHelpers/Core/GroupGenerator.cs`, `Generate` ; `Shared/ScenarioHelpers/Report/WordReportBuilder.cs`, `AppendImplantation`.

## Chapitre 2 — Le vocabulaire, les repères et la grille

### 2.1 Mother, palonnier, point d'accroche, travée

Quatre mots, et une seule chose à retenir : ils ne sont pas interchangeables, et ils ne vont pas dans le même sens.

**Point d'accroche.** Un point de la salle, sur une poutre bois, désigné par une lettre de poutre et un numéro de ligne de points : `E5`, `D3`, `I9`. Il est fixe. C'est le seul endroit où une charge finit par arriver.

**Ligne de mother** (classe `Mother`). Un pont continu **en travers des poutres**, au-dessus d'une ligne de points, posé sur chaque poutre qu'il traverse. Une ligne de mother appartient à une ligne de points, de 1 à 9, et s'étend sur une suite de travées. Elle est assemblée de pièces d'un seul modèle de `MotherGrill.xlsx`.

**Travée** (`bay`). L'intervalle entre deux poutres voisines sous une même ligne de mother : `F-E`, `E-D`. C'est l'unité de vérification : chaque travée est jugée seule, avec la somme des charges qui tombent strictement à l'intérieur, contre la valeur admissible du modèle à la portée de cette travée. Une ligne de mother de trois travées n'est pas vérifiée sur sa longueur totale.

**Palonnier.** Un pont **le long des poutres**, d'une ligne de points à la suivante, suspendu aux lignes de mother qu'il croise. C'est lui qui permet d'accrocher une charge là où la salle n'a rien. Il est assemblé de pièces d'un seul modèle de `Palonnier.xlsx`.

**Pré-rigg.** Le nom que l'étude donne à l'ensemble mother + palonniers montés avant la production. Le tableau « Ponts pour le mother et le pré rigg » du rapport en donne la composition pièce par pièce.

En plan, vu depuis le sol, avec les poutres verticales et les lignes de points horizontales :

```
  ligne 2  ═════╤═══════════╤═══════════╤═════   ligne de mother (travées F-E, E-D)
                │           │           │
                │      ██ palonnier  (x = 1 m, portée 3,650 m)
                │      ●  charge S1, 550 kg
                │           │           │
  ligne 3  ═════╧═══════════╧═══════════╧═════   ligne de mother
                F           E           D
                     poutres (écart ≈ 7,2 m)
```

> Un palonnier n'est pas un mother court. Ils ne portent pas dans le même sens, ne se lisent pas dans le même fichier d'inventaire, et ne se vérifient pas pareil : le palonnier est jugé sur sa portée entre accroches, la ligne de mother travée par travée.

**À lire :** `Shared/ScenarioHelpers/Models/Models.cs`, classes `Mother` et `Palonnier` ; `Shared/ScenarioHelpers/Core/MotherLines.cs`, `Plan`.

### 2.2 Les deux systèmes d'axes, et pourquoi il y en a deux

Il y a deux repères dans cette application, et c'est délibéré.

**Les axes de l'étude** sont ceux que les études manuscrites emploient (Figure 2 de chacune) : origine au **Point0**, qui est `E5` à l'Antarès ; **X le long des poutres, positif vers le point 9** ; **Y en travers des poutres, positif vers la poutre J**. C'est ce qu'une personne lit, tape et dessine : la page Charges, le plan SVG, `Charges.xlsx`, l'import d'éléments, le DXF, l'étude Word, le rapport Excel.

**Les axes de la grille** sont ceux des calculs : **X en travers des poutres, positif vers la poutre A** ; **Y le long des poutres, positif vers le point 1**. Ce sont ceux de `CharpenteCoordinates`, de l'optimiseur, de la transmission des charges, et des colonnes `X`, `Y`, `X1`…`Y2` **stockées en base**.

Les deux repères ont la même origine et diffèrent d'une réflexion :

```
étude  →  grille :  (x, y) → (−y, −x)
grille →  étude  :  (x, y) → (−y, −x)
```

C'est la même formule dans les deux sens. La transformation est **sa propre réciproque** : l'appliquer deux fois rend ce qu'on lui a donné. C'est commode, et c'est aussi le piège : une conversion appliquée une fois de trop ne provoque aucune erreur, elle renvoie simplement toutes les charges de l'autre côté de `E5`, en silence.

#### Une conversion faite à la main

Prenons la charge `L1` du fichier d'exemple : `X = −7,45`, `Y = −2,99`, en axes étude.

```
grille X = − (étude Y) = − (−2,99) = +2,99 m
grille Y = − (étude X) = − (−7,45) = +7,45 m
```

Stocké : `X = 2,99`, `Y = 7,45`. Relecture : la ligne de points la plus proche de y = 7,45 est la ligne 3, à 7,400 ; l'écart est de 0,05 m, donc `L1` pend directement du mother de la ligne 3. Et x = 2,99 tombe entre la poutre E (0) et la poutre D (+7,144) : la charge est dans la travée E-D.

#### Où la conversion a lieu, et où elle ne doit surtout pas avoir lieu

| Endroit | Sens | Ce qui est fait |
|---|---|---|
| Page Charges (colonnes X et Y) | grille ↔ étude | `ChargesScenique.StudyX` / `StudyY`, propriétés `[NotMapped]` |
| Pages MotherGrid / Palonnier | grille ↔ étude | `MotherGridPalonnier.StudyX1`…`StudyY2`, `[NotMapped]` |
| Lecture d'un classeur de charges | étude → grille | `ExcelLoader.ReadCharges`, `StudyAxes.ToGrid` |
| Lecture d'un classeur d'éléments | étude → grille | `ExcelLoader.LoadMotherGridPalonniers`, `StudyAxes.ToGrid` |
| Messages de refus du plan | grille → étude | `StudyAxes.StudyX(row.Y)`, affichage seulement |
| Construction des `Charge` de l'optimiseur | aucune | copie directe de `X` et `Y` |

Cette dernière ligne est la plus importante. Dans `Pipeline.GenerateLayout`, les lignes `ChargesScenique` sont recopiées telles quelles dans `RiggingOptimizer.Models.Charge` : `X = c.X`, `Y = c.Y`. Les deux sont déjà dans les axes de la grille, et l'optimiseur travaille dans les axes de la grille. Convertir ici retournerait tout le plan par rapport à `E5` — et rien, en aval, ne pourrait s'en apercevoir.

Les valeurs stockées sont passées aux axes de la grille **une seule fois**, par une migration au démarrage, protégée par le réglage `coords.axes` : elle sauvegarde les anciennes valeurs dans `ChargesSceniqueAxesBackup`, puis exécute `UPDATE dbo.ChargesScenique SET X = -Y, Y = -X;`. SQL Server évalue les deux membres de droite sur la ligne d'origine : c'est bien la réflexion, pas deux affectations successives. Les implantations calculées avant cette migration doivent être régénérées.

> Le nom `PalonnierDirection.Y` désigne l'axe **de la grille**. Un palonnier « Y » est à x constant en grille, donc le long des poutres — ce qui, dans les axes de l'étude, correspond à un Y constant et à un X qui varie. Lire « Y » comme « vertical sur le plan de l'étude » est faux.

**À lire :** `Shared/StudyAxes.cs`, en entier ; `Shared/Data/Models/ChargesSceniqueStudyAxes.cs` et `MotherGridPalonnierStudyAxes.cs` ; `Shared/ScenarioHelpers/PlanSource.cs`, `Read` (construction des `Charge`) ; `AliantWebS/Program.cs`, `EnsureDatabaseSchema`.

### 2.3 La grille de l'Antarès en chiffres

Les coordonnées relevées sur site sont dans `CharpenteCoordinates`, **en millimètres**, et exposées en mètres par la propriété `Points` (division par 1 000). `Geometry` en tire la grille de travail : les X des poutres sont lus sur la ligne 5 (`"{col}5"`) et ramenés à `E5` ; les Y des lignes de points sont lus sur la colonne E (`"E{r}"`) et laissés tels quels, `E5` étant déjà à y = 0.

Poutres — X en grille, Y en étude :

| Poutre | X grille (m) | Y étude (m) | Écart à la précédente (m) |
|---|---|---|---|
| I | −29,306 | +29,306 | — |
| H | −22,106 | +22,106 | 7,200 |
| G | −14,406 | +14,406 | 7,700 |
| F | −7,156 | +7,156 | 7,250 |
| E | 0 | 0 | 7,156 |
| D | +7,144 | −7,144 | 7,144 |
| C | +14,394 | −14,394 | 7,250 |
| B | +22,094 | −22,094 | 7,700 |
| A | +29,294 | −29,294 | 7,200 |

Lignes de points — Y en grille, X en étude :

| Ligne | Y grille (m) | X étude (m) | Écart à la précédente (m) |
|---|---|---|---|
| 1 | +15,150 | −15,150 | — |
| 2 | +11,050 | −11,050 | 4,100 |
| 3 | +7,400 | −7,400 | 3,650 |
| 4 | +3,700 | −3,700 | 3,700 |
| 5 | 0 | 0 | 3,700 |
| 6 | −3,700 | +3,700 | 3,700 |
| 7 | −7,450 | +7,450 | 3,750 |
| 8 | −11,050 | +11,050 | 3,600 |
| 9 | −15,151 | +15,151 | 4,101 |

Ce qu'il faut en retenir, chiffres en main :

- Les travées entre poutres vont de **7,144 m** (E-D) à **7,700 m** (H-G et C-B). Une ligne de mother doit franchir au moins 7,144 m pour joindre deux poutres.
- Les écarts entre lignes de points vont de **3,600 m** (lignes 8-7) à **4,101 m** (lignes 9-8). Un palonnier doit franchir au moins 3,600 m.
- La grille n'est pas régulière : les écarts ne sont pas égaux, et elle n'est pas non plus symétrique — la ligne 9 est à −15,151 m quand la ligne 1 est à +15,150 m, soit 1 mm de plus.
- `E5` est à **33,103 m** de `J5`, mesurés en travers des poutres. C'est l'origine de tout.

C'est de ces écarts que se sert le message de refus quand l'inventaire est trop court : « les poutres sont espacées de 7,14 à 7,7 m » et « deux lignes de points sont espacées d'au moins 3,6 m » sont calculés à l'exécution à partir de ces listes, pas écrits en dur.

> `Geometry.Cols` vaut `{ "I","H","G","F","E","D","C","B","A" }` : **la poutre J n'est pas dans la grille de l'optimiseur, et c'est voulu.** Elle existe pourtant dans `CharpenteCoordinates` (`J5` à 0 mm, soit −33,103 m en grille) et dans le rapport, dont `ReportDataBuilder.Beams` compte dix poutres, de J à A. L'optimiseur travaille donc sur 9 poutres et 8 travées. La raison est désormais écrite dans `Geometry.cs`, avec les chiffres des six études manuscrites qui la fondent, et le § 12.1 la reprend en entier — avec la dissymétrie qu'elle laisse : le calcul de charge, lui, connaît J.

`Geometry` ne garde que ce qui est lu. Les deux espacements moyens `DX` et `DY`, les deux dictionnaires `ColX` et `RowY` — qui n'étaient qu'une étape vers `ColXList` et `RowYList` — et la fonction `EnclosingCols` ont été retirés du fichier : personne ne les appelait. Le rattachement d'un point à une travée passe par `MotherLines.Plan` et `MotherLines.LineAt`, et les trois commentaires de `PalonnierHanging.cs` qui citaient encore `EnclosingCols` comme la règle en vigueur nomment maintenant `MotherLines.Plan` et le débord que portent `Mother.XL` et `Mother.XR`. Ce qui reste et sert : les deux listes triées `ColXList` et `RowYList`, `NearestRow`, `EnclosingRows`, `ProjOnAxis` et `Reactions2Pt`. `Geometry.Grid`, la grille complète (col, ligne) → (x, y), existe toujours et n'est lue nulle part.

**À lire :** `Shared/Data/Models/CharpenteCoordinates.cs` ; `Shared/ScenarioHelpers/Core/Geometry.cs`, l'en-tête du fichier et le constructeur statique ; `Shared/ScenarioHelpers/Pipeline.Messages.cs`, `UncoveredMessage`.

### 2.4 Les tolérances, une par une

Une tolérance n'est pas une marge d'erreur : c'est une décision. De chaque côté du seuil, le code fait autre chose.

| Constante | Valeur | Où | Ce qu'elle décide |
|---|---|---|---|
| `Geometry.TOL` | 0,08 m | `Geometry.cs` | même x, sur une ligne de points, sur une poutre |
| `Geometry.CrossingTol` | 0,080001 m | `Geometry.cs` | jusqu'où au-delà de l'extrémité dessinée d'un mother un palonnier le croise encore |
| `Geometry.ColinearTol` | 0,20 m | `Geometry.cs` | alignement d'un groupe biais de 3 ou 4 charges |
| `Mother.OnSupportM` | 0,010 m | `Models.cs` | une charge si près d'une poutre est *sur* la poutre |
| `LoadTable.LengthToleranceM` | 0,01 m | `LoadTable.cs` | une ligne d'abaque un peu plus courte couvre quand même la portée |

#### `TOL` = 0,08 m — huit centimètres

C'est la tolérance de position, et de loin la plus employée. Physiquement : huit centimètres, c'est ce qu'on ne discute pas sur un plan de production à l'échelle. Elle sert à trois décisions distinctes.

*Une charge est-elle sur une ligne de points ?* `Math.Abs(c.Y - ry) <= TOL`. À 0,08 m, la charge pend directement du mother ; à 0,081 m, elle exige un palonnier — donc un pont, des pièces prises au stock, et une accroche à deux lignes au lieu d'une. La bascule est brutale, et c'est voulu. Dans le fichier d'exemple, `L1` est à 0,05 m de la ligne 3 : elle passe. Si le plan l'avait donnée à −7,52 m en étude (soit y = 7,52 en grille), l'écart serait de 0,12 m et l'optimiseur construirait un palonnier pour elle.

*Deux charges ont-elles le même x ?* Dans `GroupGenerator`, les charges sont réparties en paquets de x voisins à `TOL` près. Attention : le paquet garde le x **de la première charge rencontrée**, et les extrémités A et B du palonnier sont posées à ce x-là. Deux charges à x = 1,00 et x = 1,07 donnent donc un palonnier à x = 1,00, et la seconde est projetée dessus à 7 cm près. Le code ne fait pas de moyenne.

*Une extrémité tombe-t-elle sur une poutre ?* Dans `MotherLines.Plan`, une demande à moins de `TOL` d'une poutre ne réclame pas de travée pour elle-même : elle se rattache à une travée voisine déjà nécessaire, ou, à défaut, à celle qui va vers le milieu de la grille.

#### `CrossingTol` = `TOL + 1e-6` = 0,080001 m

Un palonnier peut croiser une ligne de mother un peu au-delà de son extrémité dessinée. La question est : jusqu'où ? Réponse : `CrossingTol`. Le micromètre ajouté n'a aucun sens physique ; il absorbe les arrondis, pour que le calcul et l'optimiseur ne se contredisent jamais sur un croisement exactement à la limite. C'est **une seule constante**, lue par `Optimizer.HangOnCrossings` et par `Helpers.PalonnierHanging` (`CrossingToleranceM`) : tout croisement que le calcul trouve est un croisement que l'optimiseur a tranché.

À la limite : un croisement à 0,08 m au-delà de l'extrémité est retenu, et la charge du palonnier y entre ; à 0,09 m, la ligne n'est pas candidate, le palonnier ne s'y accroche pas, et sa charge va ailleurs. Un point d'accroche peut ainsi passer de quelques centaines à plus de mille kilos pour huit centimètres.

Dans le même esprit, `Optimizer.OnLine` **ramène** un point suspendu dans les bornes de la ligne : `Math.Clamp(x, line.XL, line.XR)`. Un point placé quelques centimètres au-delà de l'extrémité est traité comme s'il était à l'extrémité — la géométrie du palonnier, elle, n'est pas déplacée.

#### `ColinearTol` = 0,20 m

Vingt centimètres, dix fois plus grossier que `TOL`, et pour une raison : un pont a de la largeur, et un plan de production n'aligne jamais trois moteurs au centimètre. Un groupe biais de 3 ou 4 charges n'est retenu que si **toutes** ses charges sont à moins de 0,20 m de l'axe joignant la charge la plus basse en y à la plus haute. À 0,21 m, le groupe est rejeté et les charges seront portées séparément.

Le code mesure l'écart comme une distance de point à droite :

```
écart = | (bx − ax)·(cy − ay) − (by − ay)·(cx − ax) | / ‖AB‖, où A est la charge la plus basse en y, B la plus haute et C la charge mesurée.
```

L'axe est imposé : c'est celui du point le plus bas au point le plus haut en y, celui-là même sur lequel les charges seront projetées et que l'étude mesurera. Une autre droite passant par le nuage (par exemple celle des deux points les plus éloignés en x) pourrait passer près d'une charge que cet axe-là manque. L'étude de charges reprend la même valeur, augmentée d'un centimètre : `ReportDataBuilder.AcrossPalonnierToleranceM` = `ColinearTol` + 0,01 = 0,21 m.

Deux garde-fous accompagnent les biais : la taille d'un groupe biais est plafonnée à **4** charges, et un groupe dont l'étendue en x ou en y est inférieure à `TOL` est écarté — le premier cas est déjà traité comme groupe Y, le second (même y) est simplement ignoré. Il n'existe donc **aucun palonnier en travers des poutres** : ce rôle est celui du mother.

#### Les deux voisines, à ne pas confondre

`Mother.OnSupportM` = **0,010 m** : une charge à moins d'un centimètre d'une poutre est considérée comme portée par cette poutre, et ne fléchit aucune des deux travées voisines. C'est la convention des études. `LoadTable.LengthToleranceM` = **0,01 m** : une ligne d'abaque de 8,399999999999999 m couvre bien une portée de 8,40 m — les longueurs des classeurs sont issues de formules et traînent du bruit flottant.

**À lire :** `Shared/ScenarioHelpers/Core/Geometry.cs` ; `Shared/ScenarioHelpers/Core/GroupGenerator.cs`, `WithinAxis` et `GenerateBiaisGroups` ; `Shared/ScenarioHelpers/Core/Optimizer.cs`, `OnLine` et `HangOnCrossings` ; `Shared/ScenarioHelpers/Models/Models.cs`, `Mother.BayLoadsOf`.

## Chapitre 3 — Ce qui entre dans l'optimiseur

### 3.1 Le plan de charges, depuis la page Charges

Le plan de charges **est** la page Charges. `Générer l'implantation` lit les lignes `ChargesScenique` du scénario, jamais `Charges.xlsx`. Ce point a une histoire : tant que le placement lisait le classeur, toute charge ajoutée, déplacée ou repesée sur la page restait invisible au placement. `Charges.xlsx` n'est plus qu'un moyen de remplir la page, par le bouton « Import Excel ».

Une ligne `ChargesScenique` porte : `Name` (50 caractères au plus), `Numero`, `X`, `Y` (axes de la grille, nullables) et `Charge` en kilogrammes. Sur la page, les colonnes X et Y n'affichent pas `X` et `Y` mais `StudyX` et `StudyY`, les propriétés non mappées qui font la conversion (§ 2.2).

Trois précautions encadrent cette lecture, et chacune répare un incident réel :

1. **La génération possède son propre contexte.** `GenerateLayout` ouvre `entitiesService.CreateOwnedContext()` — un contexte tiré de la fabrique, à lui seul, dont il dispose à la fin — et écrit tout par lui. Le contexte partagé du circuit Blazor, celui que les pages éditent, n'est plus touché du tout. C'est ce qui règle le problème pour de bon : une ligne de la page Charges en cours de frappe est suivie par le contexte de la page, pas par celui-ci, et aucun `SaveChanges` du placement ne peut l'enregistrer sans les contrôles de la page ni échouer à cause d'elle. L'ancien `entitiesService._appDBContext.ChangeTracker.Clear()` du tout début **n'existe plus** : ce n'était qu'un pansement sur un contexte partagé.
2. La lecture se fait en `AsNoTracking()`, parce que `ReplaceElementsAsync` vide le tracker à l'intérieur d'une stratégie de reprise qui rejoue tout son bloc.
3. L'ordre est figé : `OrderBy(Numero ?? int.MaxValue)` puis `ThenBy(Name, NaturalStringComparer)`. L'optimiseur étant glouton, il remplit chaque pont avec les charges dans l'ordre où il les rencontre : sans ordre imposé, deux exécutions sur le même plan donneraient deux implantations.

Enfin, la copie vers `RiggingOptimizer.Models.Charge` est **directe** : `X = c.X`, `Y = c.Y`, `Weight = c.Charge`, sans conversion d'axes.

**À lire :** `Shared/ScenarioHelpers/PlanSource.cs`, `Read`, en entier ; `Shared/ScenarioHelpers/Pipeline.cs`, `GenerateLayout` (l'ouverture du contexte) ; `Shared/Data/EntitiesService.cs`, `CreateOwnedContext` ; `Shared/Data/Models/ChargesScenique.cs` ; `AliantWebS/Pages/LoadPages/ChargesSceniques.razor`, `UpdateRow`.

### 3.2 L'inventaire : deux classeurs, huit colonnes

L'inventaire, lui, reste sur le disque : `MotherGrill.xlsx` pour les mothers, `Palonnier.xlsx` pour les palonniers. Le dossier `data` du scénario est fouillé d'abord, puis le dossier partagé `_common/data`. La recherche ignore la casse du nom — Windows l'ignore, Linux non, et l'image Docker tourne sous Linux : un fichier `mother_alc500.xlsx` déposé par un navigateur était ignoré et la copie partagée utilisée à sa place, avec d'autres capacités. Si un dossier contient deux fichiers dont les noms ne diffèrent que par les majuscules, la lecture refuse : on ne peut pas deviner lequel était voulu.

Chaque ligne est une **référence** de pont. Huit colonnes :

| Colonne | En-tête | Obligatoire | Ce qu'elle vaut |
|---|---|---|---|
| A | Modèle | oui | le nom de la référence ; devient un `ElementType` |
| B | Longueur | oui | la longueur d'une pièce, en mètres |
| C | Nombre | oui | combien de pièces en stock |
| D | Poids propre | non | la masse d'**une** pièce, en kg ; vide = inconnue |
| E | Charge ponctuelle au milieu | oui | ce que les charges sur la pièce peuvent totaliser, en kg |
| F | Matériau | non | pour l'étude |
| G | Marque | non | nomme la référence dans l'étude |
| H | Usage | non | confronté au fichier où la ligne se trouve |

Les lignes d'un même modèle, rangées par longueur, forment son **abaque**.

#### En-tête ou pas d'en-tête : le fichier décide

La première feuille est lue. Si la première ligne contient au moins un des huit en-têtes connus, c'est un en-tête : les colonnes sont retrouvées par leur nom, dans n'importe quel ordre, et une colonne obligatoire absente est une **erreur** — pas une ligne de données, car un en-tête mal orthographié lu comme une donnée entrerait le mot « Modèle » comme modèle de pont. Sinon, le fichier est lu **sans en-tête**, colonne par colonne dans l'ordre A…H ci-dessus, dès sa première ligne, avec l'avertissement correspondant. La comparaison des en-têtes ignore la casse, les accents, les espaces multiples et l'unité entre parenthèses : `Longueur (m)` et `Longueur` désignent la même colonne.

#### `Nombre = 0` : une valeur, jamais une pièce

C'est la règle la moins intuitive du fichier. Une ligne à `Nombre = 0` ne fournit **aucune pièce** : elle n'existe que pour donner une charge admissible à une longueur que l'abaque doit connaître. Le classeur d'exemple en contient une :

```
S500 Carrée Acier | 1,4 | 5  | 28,8 | 8000
S500 Carrée Acier | 2,8 | 9  | 47,8 | 5733
S500 Carrée Acier | 5,6 | 39 | 95,6 | 3429
S500 Carrée Acier | 7,0 | 0  | 96   | 2000   ← valeur seulement
```

Sans cette dernière ligne, l'abaque du modèle s'arrêterait à 5,6 m. Avec elle, une travée de 7,144 m (la travée E-D) est lue au-delà de la plus longue ligne du modèle et sa valeur est **calculée** à moment égal à partir de 7,0 m et de 96 kg : p = 96 / 7 = 13,71 kg/m, M = 2 000 × 7 / 4 + 13,71 × 7² / 8 = 3 500 + 84 = 3 584, puis P = 4 × (3 584 − 13,71 × 7,144² / 8) / 7,144 ≈ **1 958 kg**. Le mécanisme de l'abaque est détaillé plus loin ; ce qui compte ici, c'est qu'une ligne sans stock n'est pas une ligne inutile.

Trois cas voisins, à ne pas confondre :

- **Nombre vide** : « *Nombre vide ; la ligne ne donne qu'une charge ponctuelle au milieu, aucune pièce n'est en stock.* » Lue comme 0, mais signalée, car un vide est plus souvent un oubli qu'une intention.
- **Nombre négatif** : « *Nombre négatif, lu comme 0.* »
- **Nombre = 0** : aucun avertissement. C'est la façon normale d'écrire une valeur d'abaque.

#### Les avertissements ligne par ligne

| Situation | Effet | Message |
|---|---|---|
| Longueur illisible ou ≤ 0 | ligne ignorée | « *…, ligne N (« modèle ») : longueur illisible, ligne ignorée.* » |
| Charge ponctuelle au milieu illisible ou ≤ 0 | ligne ignorée | « *… : charge ponctuelle au milieu illisible, ligne ignorée.* » |
| Usage incohérent avec le fichier | ligne **gardée** | « *… : usage « X » dans le fichier des mothers ; la ligne est lue comme un de ces mothers.* » |
| Même modèle, même longueur, deux fois | les deux gardées | « *… figure plusieurs fois. La plus faible charge ponctuelle au milieu est retenue ; les pièces de chaque ligne restent en stock.* » |
| Feuille vide | rien lu | « *MotherGrill.xlsx est vide.* » |
| Modèle absent, cellule `Modèle` vide | ligne sautée, sans message | — |

Une ligne ignorée n'est pas anodine : son modèle perd cette longueur. L'abaque lira la ligne plus longue suivante, ou calculera au-delà d'une ligne plus courte, et le catalogue n'aura ni masse ni stock pour elle. C'est pourquoi ces lignes sont aussi rendues comme données (`Inventory.Skipped`), et pas seulement comme texte : l'étude peut alors dire qu'un modèle qu'elle vérifie a perdu une ligne.

#### Une pièce, un poids

Avant que quoi que ce soit ne lise ces lignes, `ReconcileWeights` rapproche les lignes d'un même modèle à une même longueur — dans un classeur, ou une dans chacun quand un pont sert de mother et de palonnier — qui donnent des poids propres différents. C'est une seule pièce, donc un seul poids, et c'est le **plus lourd** qui est retenu : c'est lui que les points d'accroche portent, et un pont plus lourd est moins autorisé quand une valeur est calculée au-delà de sa plus longue ligne. Un poids vide est une inconnue, pas un poids léger : il est laissé tel quel. L'avertissement nomme les deux valeurs et celle retenue.

Enfin les lignes sont rangées dans l'ordre où l'optimiseur prend le stock : les mothers par longueur croissante puis capacité décroissante, les palonniers par longueur croissante.

**À lire :** `Shared/ScenarioHelpers/InventoryFiles.cs`, `Load`, `ReadFile` et `ReconcileWeights` ; `Shared/ScenarioHelpers/ExcelCells.cs`, `Normalize` et `Headings` ; `Shared/ScenarioHelpers/InputFiles.cs`, `Find`.

### 3.3 Les refus, avant toute implantation

Tous les contrôles du plan sont faits **avant** le placement, et tous ensemble : la page affiche le refus en une seule alerte, préfixée « Génération impossible : ». Refuser à la première erreur obligerait l'utilisateur à relancer autant de fois qu'il y a de fautes.

#### Le scénario n'a aucune charge

```
Le scénario n'a aucune charge : la page Charges est vide. Ajoutez-y les
charges, ou importez-les avec le bouton « Import Excel », puis relancez la
génération. L'implantation existante n'a pas été modifiée.
```

Sans ce refus, le placement réussirait sur zéro charge, produirait zéro élément — et `ReplaceElementsAsync` remplacerait l'implantation existante par rien. Un clic malheureux effacerait le travail précédent.

#### Le plan ne peut pas être placé

Le message rassemble jusqu'à dix problèmes, séparés par « ; », et compte le reste :

```
Le plan de charges de la page Charges ne peut pas être placé : la charge en
X = -7,45 m, Y = -2,99 m (154 kg) : sans nom ; « T3 » : deux charges portent
ce nom ; … (+4). L'implantation existante n'a pas été modifiée.
```

Chaque charge est désignée par son nom entre guillemets français. Une charge **sans** nom ne peut être désignée par un numéro — l'indice de la boucle est celui du tri interne, pas celui de la page —, elle est donc décrite par ce qu'elle a : sa position et son poids, `la charge en X = -7,45 m, Y = -2,99 m (154 kg)`, en axes étude, ce que l'utilisateur lit sur la page. Une valeur absente s'écrit `?`, jamais `0` : compléter par zéro placerait la charge sur `E5` sans le dire.

Les cinq problèmes possibles :

| Problème | Message | Ce qui arriverait si on l'acceptait |
|---|---|---|
| Nom vide | `« … » : sans nom` | L'optimiseur, la `ChargeMp` stockée et l'étude ne distinguent les charges que par leur nom. Une charge sans nom devient indistinguable ; le contrôle d'équilibre tomberait plus loin, sur une table que l'utilisateur ne peut plus rattacher à ce qu'il a fait. |
| Nom en double | `« T3 » : deux charges portent ce nom` | Même cause. `carried` est un ensemble de noms : deux charges d'un nom comptent pour une seule. La seconde est portée en géométrie mais pas en masse — c'est exactement ce qui fait échouer le contrôle d'équilibre. |
| Position incomplète | `« L4 » : position incomplète (X = ? m, Y = 1,5 m)` | `X` ou `Y` est nul. Le placer supposerait un zéro, donc `E5`, et la charge serait implantée au centre de la salle sans que personne ne l'ait demandé. |
| Charge manquante | `« L4 » : charge manquante` | Poids nul en base ≠ poids zéro. Un pont serait dimensionné sur un total faux et l'étude imprimerait ce total. |
| Charge négative | `« L4 » : charge négative (−120 kg)` | Un poids négatif soulagerait une travée : une ligne de mother serait acceptée là où elle est en réalité surchargée. |

> **0 kg est accepté.** Un point de charge qui ne pèse rien doit quand même être atteint par un pont, et l'étude le liste. C'est l'absence de valeur, et la valeur négative, qui sont refusées.

Un détail à connaître : les coordonnées affichées dans le message sont converties depuis les axes de la grille. `X` étude est calculé depuis `row.Y` et `Y` étude depuis `row.X`. Une ligne dont le `Y` stocké manque affiche donc `X = ?` — ce qui est juste, mais surprend au premier regard.

#### Un fichier d'inventaire manque

```
Fichier d'entrée manquant : Palonnier.xlsx. Ajoutez-le aux fichiers du
scénario, ou au dossier partagé _common/data.
```

Le classeur des charges n'est jamais nommé ici : le placement ne l'ouvre pas. Cette exception est **relancée** ; auparavant elle était avalée, et une génération ratée ressemblait exactement à une génération réussie — le spinner s'arrêtait, le dossier de sortie restait vide.

Deux refus voisins viennent du contenu : « *Palonnier.xlsx ne contient aucune ligne lisible.* » et « *MotherGrill.xlsx ne contient aucune ligne lisible.* », levés quand toutes les lignes du classeur ont été écartées.

#### Après le placement : deux refus de dernière minute

Ces deux-là arrivent trop tard pour être des contrôles d'entrée, mais ils protègent la même chose.

```
3 charge(s) sur 24 ne sont portées ni par un palonnier ni par un mother :
S1 (550 kg), S2 (550 kg), S3 (550 kg). Même assemblé de toutes ses pièces, le
plus long palonnier de Palonnier.xlsx mesure 3 m, alors que deux lignes de
points sont espacées d'au moins 3,6 m. L'implantation existante n'a pas été
modifiée.
```

Le message nomme jusqu'à huit charges, puis diagnostique le stock : la plus longue barre que chaque fichier pourrait assembler, toutes pièces d'un même modèle bout à bout, dans la limite de `Assembler.MaxPieces` = **16** pièces. Si ce maximum reste sous le plus petit écart entre deux lignes de points diminué de `TOL` (3,600 − 0,08 = 3,520 m) ou sous la plus petite travée diminuée de `TOL` (7,144 − 0,08 = 7,064 m), la cause n'est pas le plan : c'est l'inventaire. Jusqu'à trois erreurs de lignes de mother sont rappelées ensuite, et, si le réglage `optimizer.orthogonalOnly` est actif, la phrase « *Le réglage « orientations orthogonales uniquement » est activé : aucun palonnier incliné n'a été proposé.* » est ajoutée — car c'est souvent l'explication.

```
Les points d'accroche portent 2366 kg pour 2516 kg de charges : l'implantation
calculée n'est pas cohérente et n'a pas été enregistrée. L'implantation
existante n'a pas été modifiée.
```

Toutes les charges déclarées portées, et pourtant les points d'accroche n'en portent pas autant : le résultat se contredit lui-même. C'est le filet de sécurité du cas « deux charges sous un même nom ».

#### Le cas particulier de l'import Excel

Quand les charges viennent d'un classeur, c'est `ExcelLoader.ReadCharges` qui contrôle, avec les mêmes principes et un message à lui : « *Le fichier des charges n'a pas pu être lu : ligne 7 : charge sans nom ; « L3 » : deux charges portent ce nom, aux lignes 4 et 9 ; ligne 11 (« S2 ») : X, Y illisibles.* » Ici les lignes du tableur peuvent être nommées, ce qui n'est pas possible sur la page. L'import, lui, ne remplace jamais une ligne existante : il la garde et le dit, « *N charge(s) du fichier portent un nom déjà présent sur la page… Les valeurs de la page ont été conservées* », et refuse les noms de plus de **50** caractères, longueur de la colonne en base.

**À lire :** `Shared/ScenarioHelpers/PlanSource.cs`, `Read` (la boucle de contrôle) ; `Shared/ScenarioHelpers/Pipeline.Messages.cs`, `UncoveredMessage` ; `Shared/ScenarioHelpers/Pipeline.Imports.cs`, `ImportChargesSceniquesFromStream` ; `Shared/ScenarioHelpers/ExcelLoader.cs`, `ReadCharges` ; `AliantWebS/Pages/AntaresConceptionLayout.razor`, `GenerateLayout`.

## Chapitre 4 — La charge admissible : la règle, et l'abaque qui la porte

Un pont de structure ne porte pas « une charge ». Il porte une charge *à un endroit*, sur *une portée*. Le même pont qui admet 8 000 kg sur 1,4 m n'en admet plus que 2 000 sur 7 m : ce n'est pas la résistance de la barre qui change, c'est le bras de levier. Tout ce chapitre découle de là.

L'inventaire ne donne qu'un seul chiffre par ligne : la **charge ponctuelle au milieu**. C'est la charge unique, suspendue au milieu, qu'un pont de cette longueur admet. Le rigg réel, lui, n'a presque jamais une seule charge au milieu : il a trois moteurs répartis quelque part entre deux points d'accroche. Il a donc fallu une règle pour passer de l'un à l'autre. Cette règle tient en une phrase, elle est du client, et elle est écrite en tête de `Shared/ScenarioHelpers/LoadTable.cs`.

### 4.1 La règle, dans les mots du client

La règle, arrêtée le 11 septembre 2026 :

> Les charges situées entre deux points d'accroche voisins, **où qu'elles se trouvent entre eux**, peuvent totaliser la charge ponctuelle au milieu du même modèle, lue à la longueur **immédiatement supérieure** à la portée.

Trois choses s'y cachent, et chacune est une décision.

**Première décision : la position ne compte pas.** Deux moteurs de 800 kg à 30 cm d'un point d'accroche sollicitent bien moins le pont que les mêmes 1 600 kg au milieu. La règle les traite pareil. C'est volontairement grossier, et volontairement dans le sens sûr : le cas le plus défavorable est le milieu, et c'est celui-là qu'on compare. Le code ne regarde donc jamais la position d'une charge à l'intérieur d'une travée — il ne fait qu'additionner.

**Deuxième décision : toutes les lignes comptent, en stock ou non.** `LoadTable.From` retient toute ligne qui a une longueur et une valeur, sans regarder la colonne `Nombre`. Une ligne à `Nombre = 0` n'est jamais une pièce, mais elle donne une valeur. Dans `MotherGrill.xlsx` livré en exemple, le modèle *S500 Carrée Acier* a une ligne de 7 m avec `Nombre = 0`, `Poids propre` 96 kg, `Charge ponctuelle au milieu` 2 000 kg. Elle n'existe que pour être lue. On verra en 4.4 qu'elle change tout.

**Troisième décision : deux lignes à la même longueur, la plus faible valeur.** `LoadTable.From` groupe par longueur arrondie au centimètre puis prend `OrderBy(r => r.Capacity).First()`.

Enfin, deux points d'unités qu'il faut avoir en tête avant de lire un seul chiffre :

- L'inventaire est en **kilogrammes**, l'étude imprime des **daN**. Il n'y a aucune conversion dans le code : la pratique du métier ici tient 1 kg pour 1 daN, et c'est écrit en tête de `Shared/ScenarioHelpers/Report/ReportModel.cs`. 2 000 kg dans le tableur deviennent « 2 000 daN » dans le Word, à l'identique.
- La charge ponctuelle au milieu est une **charge utile, hors poids propre du pont**. C'est pour cela que le calcul d'une travée de mother n'ajoute jamais le poids du mother lui-même à la charge de la travée. L'étude le dit noir sur blanc, parce que les charges aux points d'accroche, elles, comprennent le poids des ponts, et qu'un lecteur essaierait sinon de réconcilier deux chiffres qui ne parlent pas de la même chose.

```
Les charges ponctuelles au milieu données par l'inventaire sont des charges utiles, hors
poids propre du pont : les valeurs comparées ci-dessous sont donc les seules charges
appliquées. Le poids propre des ponts est en revanche compris dans les charges aux points
d'accroche.
```

Sources : `Shared/ScenarioHelpers/LoadTable.cs`, `LoadTable.From` et l'en-tête du fichier ; `Shared/ScenarioHelpers/Report/WordReportBuilder.cs`, `AppendAbaqueChecks`.

### 4.2 Pourquoi la longueur immédiatement supérieure

Voici la question naïve : ma travée fait 2,82 m, mon inventaire donne 2,8 m et 3,0 m. Que lire ?

La réponse naïve est « on interpole ». Le code ne le fait pas, et il a raison de ne pas le faire.

Faites l'image. La valeur donnée à la longueur L, c'est la charge P que la barre supporte appliquée en son milieu. Le moment de flexion au milieu vaut alors `P·L/4`. Si la barre casse à un moment donné, alors quand L grandit, P doit diminuer — dans le même rapport. Autrement dit : **une portée plus courte est plus forte**.

```
  P·L/4 = constante   →   P diminue quand L augmente

  P
  |  *
  |     *
  |        *   .  .  .  .  .  ← valeur vraie, continue
  |            *
  +---------------------------- L
     2,8 m    3,0 m
       ↑        ↑
     2 345 kg  2 000 kg     (S500 Carrée Alu, Palonnier.xlsx)
```

Pour une portée de 2,82 m, lire la ligne de 3,0 m, c'est lire la valeur d'une barre **plus longue donc plus faible** que celle qu'on a réellement. On obtient 2 000 kg là où la barre en admet davantage. Si l'on suivait `P·L/4 = constante` depuis la ligne de 3 m, la valeur exacte à 2,82 m serait `2 000 × 3 / 2,82 = 2 128 kg`. On s'interdit donc 128 kg, soit 6 % : **de la marge, jamais du risque**.

Lire la ligne immédiatement *inférieure* ferait exactement l'inverse : on autoriserait 2 345 kg sur une portée pour laquelle la barre n'en admet que 2 128. Voilà pourquoi la règle est asymétrique, et pourquoi elle ne peut pas l'être dans l'autre sens.

Le même raisonnement explique le cas d'une portée plus courte que toutes les lignes du modèle. `At` prend alors la ligne la plus courte — la plus forte disponible — et s'arrête là. Une travée de 1,50 m d'un *S500 Carrée Alu* lit la ligne de 2,8 m et reçoit 2 345 kg. Le vrai pont en admettrait bien plus. La table ne l'invente pas : **elle n'extrapole jamais vers le bas**, seulement vers le haut (4.4).

> La table n'interpole jamais entre deux lignes, n'additionne jamais deux lignes, et ne majore jamais une valeur pour une portée courte. Un lecteur qui voit 2 345 kg sur une travée de 1,5 m ne doit pas y lire la capacité réelle du pont : il y lit la valeur d'un pont de 2,8 m, appliquée par prudence.

Sources : `Shared/ScenarioHelpers/LoadTable.cs`, `At` — la ligne `rows.FirstOrDefault(r => r.Length >= spanM - LengthToleranceM)`.

### 4.3 Lire une valeur : la table, pas à pas

`LoadTable.At(model, spanM)` procède dans cet ordre.

1. Chercher le modèle. Le dictionnaire est insensible à la casse et le nom est `Trim()`é. Aucune ligne : refus, avec le motif `« {model} » ne figure pas dans l'inventaire`.
2. Parcourir les lignes du modèle, **triées par longueur croissante, une seule par longueur**, et prendre la première telle que `r.Length >= spanM - LengthToleranceM`.
3. S'il y en a une : la valeur est sa `Capacity`, la source est `AllowedSource.Table`.
4. S'il n'y en a aucune — la portée dépasse la plus grande ligne — passer au calcul de 4.4.

La constante du point 2 mérite qu'on s'y arrête.

```
public const double LengthToleranceM = 0.01;   //  1 cm
```

Elle est là pour le bruit en virgule flottante des tableurs. Une longueur de 8,4 m saisie dans Excel revient parfois en `8.399999999999999`, et `8.399999999999999 >= 8.4` est faux. Sans tolérance, une travée de 8,4 m sauterait la ligne de 8,4 m. Le commentaire du code le dit ainsi : *a row this much shorter than the span still covers it*.

Mais elle a un effet de bord qu'il faut assumer : **une portée jusqu'à 1 cm plus longue qu'une ligne lit quand même cette ligne**. Ce n'est pas du bruit, c'est 1 cm de portée réelle qu'on offre. Sur le modèle *S500 Carrée Alu* (lignes 2,8 m → 2 345 kg et 3,0 m → 2 000 kg) :

| Portée demandée | Test `r.Length >= portée − 0,01` | Ligne retenue | Valeur |
|---|---|---|---|
| 1,50 m | 2,8 ≥ 1,49 → vrai | 2,8 m | 2 345 kg |
| 2,75 m | 2,8 ≥ 2,74 → vrai | 2,8 m | 2 345 kg |
| 2,80 m | 2,8 ≥ 2,79 → vrai | 2,8 m | 2 345 kg |
| 2,81 m | 2,8 ≥ 2,80 → vrai | 2,8 m | 2 345 kg |
| 2,82 m | 2,8 ≥ 2,81 → faux ; 3,0 ≥ 2,81 → vrai | 3,0 m | 2 000 kg |
| 3,00 m | 3,0 ≥ 2,99 → vrai | 3,0 m | 2 000 kg |
| 3,50 m | aucune ligne | — | calculée (4.4) |

Le saut se produit donc entre 2,81 m et 2,82 m, et il coûte 345 kg d'un coup. C'est la nature d'un abaque : il est en escalier, et le code ne lisse pas les marches.

La comparaison elle-même est dans `AllowedLoad.Carries` :

```
public bool Carries(double kg) => Kg is { } allowed && allowed > 0 && kg <= allowed + 1e-6;
```

Le `1e-6` absorbe les arrondis d'une somme de charges. Une charge de 2 345,0000001 kg passe ; 2 345,01 ne passe pas. Et `Known => Kg is > 0` : une valeur nulle ou négative n'est pas une valeur.

Ce que produit la lecture, l'étude l'imprime ainsi (`AllowedSentence`) :

```
D'après l'inventaire, une charge ponctuelle centrée de 2 345 daN est autorisée sur une
longueur de 2,8 m.
```

Notez qu'elle nomme **la longueur de la ligne lue**, 2,8 m, et non la portée demandée. Le lecteur peut donc retrouver la ligne dans le tableur.

Sources : `Shared/ScenarioHelpers/LoadTable.cs`, `At`, `LengthToleranceM`, `AllowedLoad.Carries` ; `Shared/ScenarioHelpers/InventoryFiles.cs`, `ReadFile` (d'où viennent les lignes) ; `Shared/ScenarioHelpers/Report/WordReportBuilder.cs`, `AllowedSentence`.

#### Deux lignes à la même longueur

Si `Palonnier.xlsx` contient deux fois *S500 Carrée Alu* à 2,8 m, l'une à 2 345 kg et l'autre à 2 200 kg, la table retient **2 200 kg**. Le lecteur d'inventaire prévient :

```
Palonnier.xlsx, ligne 9 : « S500 Carrée Alu » de 2,8 m figure plusieurs fois. La plus faible
charge ponctuelle au milieu est retenue ; les pièces de chaque ligne restent en stock.
```

La deuxième moitié de la phrase est le point subtil : la table prend une seule valeur, mais le **stock additionne les deux lignes**. Une valeur, deux tas de pièces. C'est délibéré : `LoadTable.From` groupe les lignes, `PieceStock` ne les groupe pas (chapitre 5).

Sources : `Shared/ScenarioHelpers/LoadTable.cs`, `From` ; `Shared/ScenarioHelpers/InventoryFiles.cs`, `ReadFile`, le `HashSet seen`.

#### Un poids propre, un seul, et c'est le plus lourd

Avant que quoi que ce soit ne lise l'inventaire, `ReconcileWeights` passe sur les deux classeurs. Si un même modèle à une même longueur porte deux `Poids propre` différents — dans un fichier, ou un dans chacun quand un pont sert de mother et de palonnier — **le plus lourd est retenu partout**, et une ligne d'avertissement est émise. La raison est double : c'est le poids le plus lourd que reprennent les points d'accroche, et c'est le poids qui *réduit* la valeur calculée en 4.4. Les deux vont dans le sens sûr. Une case vide n'est pas un poids plus faible : c'est un poids inconnu, et elle est laissée telle quelle.

Source : `Shared/ScenarioHelpers/InventoryFiles.cs`, `ReconcileWeights`.

### 4.4 Au-delà de la plus grande longueur : le calcul à moment égal

Le problème est réel et quotidien. Les poutres de la salle sont espacées de 7,14 à 7,70 m (E–D fait 7,144 m, H–G et C–B font 7,700 m). Si le plus long pont d'un modèle mesure 5,6 m, aucune ligne ne couvre une travée de mother. Refuser de répondre reviendrait à ne jamais vérifier un mother.

Le code répond, mais il *dérive* la réponse au lieu de l'inventer.

#### La dérivation

Une poutre sur deux appuis, de portée L, qui porte une charge ponctuelle P en son milieu, a un moment de flexion à mi-portée :

```
M_charge = P · L / 4
```

La même poutre, chargée uniformément par son propre poids p (en kg par mètre), a à mi-portée :

```
M_propre = p · L² / 8
```

La ligne d'inventaire décrit une barre de longueur L0 qui admet P0 au milieu. Son moment total à mi-portée, à la limite, vaut donc :

```
M = P0 · L0 / 4  +  p · L0² / 8
```

L'hypothèse du calcul est : **le même profil admet le même moment, quelle que soit sa longueur**. On écrit alors le même M à une portée L plus grande, on en retranche ce que le poids propre consomme déjà, et ce qui reste est disponible pour la charge :

```
P(L) · L / 4  =  M − p · L² / 8

          4 · (M − p · L² / 8)
P(L)  =  ──────────────────────
                  L
```

C'est exactement ce qu'écrit le code :

```
double p       = longest.WeightKg.Value / l0;
double moment  = p0 * l0 / 4 + p * l0 * l0 / 8;
double allowed = 4 * (moment - p * spanM * spanM / 8) / spanM;
```

Notez que `p` est déduit du `Poids propre` de la ligne, qui est la masse **d'une barre entière**, divisée par sa longueur. Pas de valeur au mètre dans l'inventaire.

#### L'arithmétique, sur une vraie ligne

Prenons *S500 Carrée Alu*, la ligne de 3 m de `Palonnier.xlsx` : `Nombre` 24, `Poids propre` 35 kg, `Charge ponctuelle au milieu` 2 000 kg. C'est la plus grande longueur du modèle (l'autre ligne fait 2,8 m). On demande la valeur à 3,5 m.

```
p  = 35 / 3                       = 11,6667 kg/m
M  = 2000 × 3 / 4                 = 1 500,000 kg·m
   + 11,6667 × 3² / 8             =    13,125 kg·m
                                  ─────────────────
M                                 = 1 513,125 kg·m

à L = 3,5 m :
p · L² / 8 = 11,6667 × 12,25 / 8  =    17,865 kg·m
M − 17,865                        = 1 495,260 kg·m
× 4                               = 5 981,042
/ 3,5                             = 1 708,9 kg
```

**1 709 kg** à 3,5 m, contre 2 000 kg à 3 m. Vérification de bon sens : la seule mise à l'échelle `P·L/4` donnerait `2 000 × 3 / 3,5 = 1 714 kg` ; le poids propre en retire 5 kg. Sur un palonnier en alu de 35 kg, le terme de poids propre est marginal — il ne le reste pas sur un mother en acier.

Le même calcul sur une travée réelle de mother, *S500 Carrée Acier*, ligne de 7 m (`Poids propre` 96 kg, 2 000 kg), travée H–G de 7,70 m :

```
p  = 96 / 7                        = 13,7143 kg/m
M  = 2000 × 7 / 4  +  13,7143 × 49 / 8  =  3 500 + 84,0  =  3 584,0 kg·m
p · L² / 8 = 13,7143 × 59,29 / 8        =            101,6 kg·m
P = 4 × (3 584,0 − 101,6) / 7,70        =          1 809 kg
```

#### Pourquoi la ligne à `Nombre = 0` n'est pas de la décoration

Retirez la ligne de 7 m de `MotherGrill.xlsx`. La plus grande ligne du modèle devient 5,6 m (3 429 kg, 95,6 kg). Refaisons la même travée de 7,144 m :

| Ligne de base | p | M | P à 7,144 m |
|---|---|---|---|
| 7,0 m — 2 000 kg — 96 kg | 13,71 kg/m | 3 584 kg·m | **1 957 kg** |
| 5,6 m — 3 429 kg — 95,6 kg | 17,07 kg/m | 4 868 kg·m | **2 664 kg** |

Une ligne de plus, sans une seule pièce en stock, fait tomber la charge admise de 2 664 à 1 957 kg — 26,5 % de moins. C'est précisément le rôle d'une ligne à `Nombre = 0` : **ancrer l'extrapolation sur une valeur constatée à une longueur proche**, au lieu de la laisser partir d'une barre courte et optimiste.

#### Les deux garde-fous

```
public const double ExtrapolationLimit = 2.0;
```

Le calcul s'arrête à **deux fois** la plus grande longueur du modèle. Pour la ligne de 3 m, il couvre jusqu'à 6 m ; la condition exacte est `spanM > 2,0 × l0 + LengthToleranceM`, donc le dernier cas accepté est 6,01 m (972 kg), et 6,02 m est refusé. Le motif de refus, en français :

```
portée de plus de 2 fois la plus grande longueur de l'inventaire (3 m)
```

Pourquoi s'arrêter ? Parce que la dérivation ne parle que de **flexion**, et qu'aux grandes portées c'est la **flèche** qui gouverne : la formule continuerait d'autoriser plus que le pont ne porte. L'en-tête de `LoadTable.cs` donne le contrôle qui a été fait contre une table constructeur : la formule reste dans le sens sûr à moins de 6 % jusqu'à trois fois la longueur de base, et se retrouve 22 % au-dessus à trois fois et demie. La limite a été mise à 2, bien en deçà du point où le calcul bascule.

Le second garde-fou : **pas de poids propre, pas de calcul**. Si la ligne la plus longue a une case `Poids propre` vide, `p` est inconnu, et le code refuse plutôt que de prendre `p = 0` — ce qui aurait *majoré* la valeur.

```
poids propre inconnu pour la longueur de 3 m, dont la valeur serait calculée
```

Il existe un troisième refus, `le poids propre seul atteint le moment admissible`, quand `allowed <= 0`. Il est en pratique inatteignable : il faudrait que le poids propre de la barre de base dépasse les deux tiers de sa charge admissible. Sur la ligne de 3 m, il faudrait 1 333 kg au lieu de 35. C'est une ceinture, pas un cas de figure.

#### Ce que l'étude imprime quand la valeur est calculée

Une valeur calculée n'est jamais présentée comme lue. L'étude Word la signale trois fois.

La note générale, imprimée une fois avant la première section d'abaque, et imprimée **même si aucun cas retenu ne l'utilise** — parce qu'un cas qui passe, donc non imprimé, peut l'utiliser :

```
Au-delà de la plus grande longueur que l'inventaire donne pour un modèle, la charge
admissible est calculée à moment égal à partir de cette longueur, jusqu'à deux fois
celle-ci, en admettant que les jonctions entre les pièces d'un pont assemblé transmettent
la totalité du moment. Cette hypothèse est acceptée par l'ingénieur signataire de l'étude.
```

La phrase du cas lui-même :

```
Cette portée dépasse la plus grande longueur de l'inventaire pour ce modèle (3 m,
2 000 daN) : la charge ponctuelle centrée admise, 1 709 daN, en est déduite à moment égal
(M = P·L/4 + p·L²/8, avec p = 11,67 daN/m).
```

Et, dans la liste des dépassements, la mention `(valeur calculée)` accolée au chiffre. L'export Excel fait de même : `F–E : 606 / 4 237 kg (calculée)`, et une colonne de provenance qui écrit `calculée à moment égal depuis la longueur de 7 m (2 000 kg, poids propre 13,71 kg/m)`.

Sources : `Shared/ScenarioHelpers/LoadTable.cs`, `At` et `ExtrapolationLimit` ; `Shared/ScenarioHelpers/Report/WordReportBuilder.cs`, `AllowedSentence`, `AppendAbaqueChecks`, `ExtrapolationWords`, `AppendExceedanceList` ; `Shared/ScenarioHelpers/ExcelExporter.cs`, `BayText` et `SourceText`.

### 4.5 Ce que la table refuse de répondre

Une table qui répond toujours est une table qui ment quelque part. Celle-ci refuse, et quand elle refuse elle dit pourquoi, en français, dans `AllowedLoad.Reason`. L'étude reprend ce texte tel quel.

| Situation | `Reason`, textuellement | `Source` |
|---|---|---|
| Le modèle n'est dans aucune ligne | `« {modèle} » ne figure pas dans l'inventaire` | `None` |
| Portée > 2 × plus grande ligne (+1 cm) | `portée de plus de 2 fois la plus grande longueur de l'inventaire ({L0} m)` | `None` |
| Poids propre vide sur la ligne la plus longue | `poids propre inconnu pour la longueur de {L0} m, dont la valeur serait calculée` | `None` |
| Le calcul donne `allowed <= 0` | `le poids propre seul atteint le moment admissible` | `None` |

Un refus n'est jamais silencieux. `CentredChecks` produit une ligne de vérification même sans valeur, `WarnNoValue` ajoute une réserve dans le document :

```
Mother M12, travée G5–F5 (7,25 m) : poids propre inconnu pour la longueur de 7 m, dont la
valeur serait calculée. La charge n'a pas pu être vérifiée.
```

Et la conclusion de section ne peut plus dire « correctement dimensionné » : elle bascule sur `Le dimensionnement des mothers n'a pas pu être entièrement vérifié pour : ...`.

Deux nuances de refus sont distinguées. `BeyondAbaque` vaut vrai quand **le modèle est bien dans l'inventaire** mais que la portée ou le poids manquant empêchent une valeur. Un modèle absent de l'inventaire, lui, est signalé **une fois** pour toute la famille, pas une fois par élément — sans quoi une faute de frappe dans un nom de type remplirait le document de la même phrase.

#### Ce que la table ne couvre pas du tout

Il faut le dire au lecteur, parce que ce sont les cas où l'on croit le plus facilement être couvert :

- **Le déport (porte-à-faux).** L'inventaire ne donne que le cas centré. Une charge au-delà du dernier point d'accroche n'est comparée à rien. Le code explique le refus par le calcul : 1 500 daN à 2,5 m en déport font 3 750 daN·m à l'encastrement, là où les valeurs centrées du même modèle impliquent environ 2 500. Lire la valeur centrée à la longueur du déport **surestimerait** ce que le pont tient. Les études existantes citent une valeur de déport (666 daN à 2,8 m) qui n'est dans aucun fichier. Chaque déport est donc imprimé comme une vérification non faite.
- **Le soulèvement.** Un palonnier en porte-à-faux tire le mother opposé vers le haut. Rien dans l'inventaire ne décrit un pont tiré vers le haut. Le cas est rapporté, pas vérifié, au-delà d'une tolérance de `UpliftToleranceKg = 0,5` kg qui n'est là que pour filtrer les arrondis.
- **Le soulagement.** Le soulèvement n'est **pas soustrait** de la charge d'une travée. Un pont tiré vers le haut en un point n'est guère soulagé de la flexion que provoquent les charges ailleurs.
- **Le poids propre du mother.** Il n'est pas ajouté à la charge de la travée, puisque la valeur de l'inventaire est une charge utile (4.1).

#### Quand une ligne d'inventaire n'a pas pu être lue

C'est le cas le plus vicieux, et il est traité explicitement. Si une ligne du modèle a été rejetée à la lecture (longueur illisible, ou charge ponctuelle illisible), elle est enregistrée dans `Inventory.Skipped` et **voyage avec les vérifications de son modèle**. La valeur obtenue n'est alors pas celle de l'inventaire : c'est celle des lignes lisibles seulement. Le document change de mots :

```
D'après les lignes lisibles de l'inventaire, une charge ponctuelle centrée de 2 000 daN est
autorisée sur une longueur de 3 m. La ligne 7 de Palonnier.xlsx, de ce modèle, n'a pas pu
être lue : cette valeur ne tient compte que des lignes lisibles et reste à vérifier.
```

et, dans les motifs de refus, `la plus grande longueur de l'inventaire` devient `la plus grande longueur lisible de l'inventaire` (`QualifiedReason`). Un contrôle dont l'inventaire est incomplet n'autorise jamais la phrase « correctement dimensionné ».

> `ReportDataBuilder.LookupAbaque` existe encore : il lit une ligne pour une portée, avec la même tolérance et la même règle du plus faible à longueur égale, **mais il ne calcule rien au-delà de la dernière ligne**. Il est conservé pour des appelants qui tiennent une liste de lignes. L'étude, elle, passe par `LoadTable`. Ne pas confondre les deux.

Sources : `Shared/ScenarioHelpers/LoadTable.cs`, `At` (chaque chaîne `Reason`) ; `Shared/ScenarioHelpers/Report/ReportDataBuilder.cs`, `CentredChecks`, `WarnNoValue`, `QualifiedReason`, `SkippedOf`, `BuildAbaqueChecks`, `BuildMotherAbaqueChecks`, `LookupAbaque` ; `Shared/ScenarioHelpers/Report/ReportModel.cs`, `AbaqueCheck` et `UnreadInventoryRows`.

## Chapitre 5 — Les pièces, les assemblages et le stock

### 5.1 Une barre n'existe pas : il y a des pièces

Une ligne de mother de G à D mesure 21,55 m. Aucun fabricant ne livre un pont de 21,55 m. Le camion contient des pièces de 1,4, 2,8 et 5,6 m, en nombre fini, et l'équipe les boulonne bout à bout.

Le code modélise exactement cela. L'objet central est `Assembly` : **des pièces d'un seul et même modèle, mises bout à bout**.

```
public sealed class Assembly
{
    public string Type { get; }                        // le modèle, un seul
    public IReadOnlyList<BarSpec> Pieces { get; }      // triées de la plus longue à la plus courte
    public int Count => Pieces.Count;
    public double LengthM => Pieces.Sum(p => p.Length);
    public double? WeightKg => ... // null si UNE pièce n'a pas de poids
}
```

Quatre choses s'y lisent, et chacune est une décision de conception.

**Un seul modèle par assemblage.** On ne mélange pas un *S500 Carrée Acier* et un *C500F / TABMA* dans le même pont. C'est ce qui permet à `LoadTable` de répondre : la table est indexée par modèle, et un assemblage a un modèle.

**Les pièces sont des lignes de stock, pas des nombres.** `Pieces` contient des `BarSpec`, c'est-à-dire les lignes mêmes de l'inventaire, par référence. Si la même pièce est utilisée quatre fois, la liste contient quatre fois la même référence d'objet. C'est ce qui permet au stock de compter (5.3).

**La masse est nulle ou complète, jamais partielle.** `WeightKg` renvoie `null` dès qu'une seule pièce n'a pas de `Poids propre`. Un pont dont on ne connaît qu'une partie de la masse n'est pas un pont plus léger : c'est un pont dont on ignore la masse. La transmission de charges le laisse alors de côté et le nomme, parce qu'une masse prise silencieusement trop basse fait lire bas **tous** les points qu'elle alimente.

**Les jonctions ne sont nulle part.** `Assembly` ne stocke aucune position de jonction. Il n'y en a pas, et il n'y en aura pas : une jonction peut tomber **en tout point de la longueur du pont**. C'est écrit dans l'en-tête du fichier (*Where the joints fall does not matter*) et répété dans l'étude :

```
Les ponts suivants sont assemblés de plusieurs pièces de leur modèle, dont les jonctions
peuvent se trouver en tout point de leur longueur :
```

Ce qui nous ramène au chapitre 4, et c'est le point le plus important de tout ce chapitre. **La charge admissible d'un pont assemblé est lue comme celle d'un pont d'une pièce.** Les 4 × 5,6 m d'un mother de G à D sont vérifiés travée par travée — 7,25 m, 7,156 m, 7,144 m — avec la table du modèle, exactement comme s'il s'agissait d'une barre continue. Cela suppose que les jonctions transmettent la **totalité** du moment. Ce n'est pas une évidence mécanique : c'est une hypothèse, elle est écrite dans le document, et l'ingénieur signataire l'accepte.

> Un lecteur pourrait croire que l'application place les jonctions au-dessus des poutres, là où le moment est faible. Elle ne le fait pas, ne l'enregistre pas, et ne l'impose pas au montage. L'hypothèse est plus forte, et plus simple : la jonction est aussi bonne que le pont.

Sources : `Shared/ScenarioHelpers/Core/Assembly.cs`, la classe `Assembly` et l'en-tête du fichier ; `Shared/ScenarioHelpers/Report/WordReportBuilder.cs`, le bloc `report.Trusses.Assembled` et la note d'abaque de `AppendAbaqueChecks`.

### 5.2 Choisir l'assemblage le moins cher

Il faut atteindre une longueur. Plusieurs jeux de pièces l'atteignent. Lequel prendre ?

Le critère naïf — « la combinaison la plus proche de la longueur voulue » — est mauvais, et pour une raison de terrain : un pont de 21,55 m fait de seize pièces de 1,4 m demande quinze jonctions à boulonner en l'air. La longueur en trop ne coûte presque rien ; les jonctions coûtent du temps, du poids de quincaillerie et du risque.

L'ordre retenu, dans `PieceStock.Compare` et dans `Assembler.Better`, est donc :

1. **Le moins de pièces.** Le montage. C'est le critère dominant, et il n'est jamais arbitré contre les autres.
2. **Le moins de longueur en trop.** À nombre de pièces égal, on ne transporte pas 3 m de pont pour rien, et un pont qui dépasse beaucoup est un pont qui dépasse quelque part dans la salle.
3. **Le plus léger.** À nombre de pièces et longueur égaux, la masse part directement dans les points d'accroche. Une pièce de masse inconnue est traitée comme **lourde** (`1e6` dans le calcul, `double.MaxValue` dans la comparaison), afin qu'une pièce de même longueur dont on connaît le poids soit préférée.
4. **Le nom du modèle**, sans tenir compte de la casse. Ce dernier critère n'a rien de mécanique : il sert à ce que le résultat ne dépende **jamais** de l'ordre des lignes dans le tableur. Deux exécutions sur le même inventaire doivent donner le même plan.

#### La recherche elle-même

`Assembler.Cheapest` n'est pas glouton : c'est une **énumération exacte**, en profondeur, sur le nombre d'exemplaires de chaque longueur.

```
target = ceil(minLengthM × 100 − 1e-6)        // en centimètres entiers
items  = lignes disponibles, triées par LONGUEUR DÉCROISSANTE, puis poids croissant
```

Tout passe en **centimètres entiers**. C'est ce qui élimine le bruit en virgule flottante : `8.399999999999999` devient 840, et deux assemblages de même longueur sont reconnus comme tels.

Trois bornes rendent la recherche finie et rapide :

- `MaxPieces = 16`. Aucun élément n'est jamais assemblé de plus de seize pièces.
- On n'essaie jamais plus d'exemplaires d'une longueur qu'il n'en faut pour combler le manque : `most = min(Available, maxPieces − count, ceil(missing / len[i]))`.
- On coupe dès que `count >= bestKey.Count` : une branche qui a déjà plus de pièces que la meilleure solution trouvée ne peut pas gagner, puisque le nombre de pièces est le premier critère. Comme la boucle essaie **le plus d'exemplaires de la plus longue pièce d'abord**, une bonne solution est trouvée tôt et cette coupe mord fort.

#### Un exemple complet : 21,55 m

Une ligne de mother de G5 à D5. G5 est à 18,697 m, D5 à 40,247 m : la ligne fait **21,550 m**. Le modèle *S500 Carrée Acier* offre des pièces de 5,6 m (39 en stock), 2,8 m (9) et 1,4 m (5).

D'abord, la longueur demandée n'est pas 21,55 m. `MotherLines.Size` demande :

```
needed = max(0,01 ; line.Length − TOL) = 21,55 − 0,08 = 21,47 m
target = ceil(21,47 × 100 − 1e-6)      = 2147 cm
```

`Geometry.TOL` vaut 0,08 m : un pont a le droit d'être 8 cm plus court que la distance dessinée, ce qui est la tolérance de position utilisée partout dans le placement.

Ensuite, une remarque qui simplifie tout : 1,4 / 2,8 / 5,6 sont tous des multiples de 1,4 m = 140 cm. **Toute somme est un multiple de 140.** Or `2147 / 140 = 15,33`, donc le premier multiple atteignable est `16 × 140 = 2240 cm = 22,40 m`. Aucune combinaison ne fait mieux en longueur : l'excès est de 93 cm dans tous les cas.

Le critère 2 ne départage donc rien. C'est le critère 1 qui tranche :

| Combinaison | Pièces | Longueur | Excès sur 21,47 m | Retenue ? |
|---|---|---|---|---|
| 4 × 5,6 | 4 | 22,40 m | 0,93 m | **oui** |
| 3 × 5,6 + 2 × 2,8 | 5 | 22,40 m | 0,93 m | non, 5 pièces |
| 2 × 5,6 + 4 × 2,8 | 6 | 22,40 m | 0,93 m | non, 6 pièces |
| 3 × 5,6 + 2,8 + 1,4 | 5 | 21,00 m | — | non, trop court |
| 16 × 1,4 | 16 | 22,40 m | 0,93 m | non, 16 pièces |

Résultat : **4 pièces de 5,6 m, soit 22,40 m de pont pour une ligne dessinée sur 21,55 m** — 0,85 m de débord réparti aux extrémités. Trois jonctions.

#### Où le critère du poids fait vraiment quelque chose

`PieceStock.Best` rejoue `Cheapest` pour **chaque modèle** que l'appelant accepte, puis compare les gagnants avec le même ordre. Reprenons les 21,47 m. `MotherGrill.xlsx` contient aussi *C500F / TABMA*, avec 10 pièces de 5,6 m et 2 de 1,4 m.

| Modèle | Assemblage | Pièces | Longueur | Masse |
|---|---|---|---|---|
| S500 Carrée Acier | 4 × 5,6 m | 4 | 22,40 m | 4 × 95,6 = 382,4 kg |
| C500F / TABMA | 4 × 5,6 m | 4 | 22,40 m | 4 × 47,8 = 191,2 kg |

Même nombre de pièces, même longueur. C'est le critère 3 qui choisit, et il choisit le *C500F / TABMA* : 191 kg de pont en moins dans les points d'accroche.

Un point de vocabulaire à ne pas rater : le filtre `modelOk` passé à `Best` est **antérieur** à tout cela. Pour un mother, c'est `MotherLines.Size` qui l'écrit :

```
bool Carries(string model)
{
    for (int k = 0; k < line.BayCount; k++)
        if (loads[k] > 1e-9 && !table.At(model, line.BayLength(k)).Carries(loads[k]))
            return false;
    return true;
}
```

Un modèle n'est candidat que si sa valeur **à chaque travée chargée** porte la charge de cette travée. Le tri « moins de pièces, moins de longueur, plus léger » ne s'exerce donc que parmi des modèles qui tiennent déjà. Pour un palonnier, c'est la même chose en une ligne, dans `Optimizer.AssemblyFor` :

```
stock.Best(Math.Max(0.01, span - TOL), model => table.At(model, span).Carries(load));
```

> Le choix est fait **au moment du placement**, avec le stock tel qu'il est à cet instant. Un palonnier choisi tôt prend les pièces ; un palonnier choisi plus tard ne voit que ce qui reste, et peut être obligé de prendre un modèle plus lourd, ou de ne pas exister du tout. Le résultat dépend donc de l'ordre de sélection gloutonne — ce n'est pas un optimum global.

Sources : `Shared/ScenarioHelpers/Core/Assembly.cs`, `Assembler.Cheapest`, `Assembler.Better`, `PieceStock.Cheapest`, `PieceStock.Best`, `PieceStock.Compare` ; `Shared/ScenarioHelpers/Core/MotherLines.cs`, `Size` ; `Shared/ScenarioHelpers/Core/Optimizer.cs`, `AssemblyFor`.

### 5.3 Le stock ne ment pas

Un optimiseur qui produirait un plan magnifique avec dix-huit ponts de 5,6 m alors que le camion en contient douze serait pire qu'inutile. `PieceStock` est la garantie que cela ne peut pas arriver.

#### Ce qui est une pièce, et ce qui n'en est pas

```
Rows = rows.Where(r => r.StockInit > 0 && r.Length > 0 && !string.IsNullOrWhiteSpace(r.Type)).ToList();
```

Une ligne à `Nombre = 0` **n'est jamais une pièce**. Elle donne une valeur à la table de charges (4.1) et rien d'autre. La ligne de 7 m de *S500 Carrée Acier*, si essentielle au chapitre 4, n'existe pas ici. Symétriquement, une ligne sans `Charge ponctuelle au milieu` lisible est rejetée dès la lecture du fichier : elle n'est ni une valeur, ni une pièce.

#### Le compteur

Le dictionnaire interne est indexé par `ReferenceEqualityComparer` : la clé est **l'objet ligne lui-même**, pas sa valeur. Deux lignes du tableur qui donnent le même modèle à la même longueur sont donc deux entrées distinctes, et leurs stocks s'additionnent — c'est le revers de la règle de 4.3, où la table n'en retenait qu'une valeur.

Cinq opérations, et c'est tout :

| Opération | Effet |
|---|---|
| `Left(row)` | Ce qu'il reste de cette ligne |
| `Used(row)` | `row.StockInit − Left(row)` |
| `CanTake(a)` | Vrai si chaque pièce de l'assemblage est disponible en assez d'exemplaires |
| `Take(a)` | Décrémente. **Lève une exception** si `CanTake` est faux |
| `Give(a)` | Rend les pièces, plafonné à `StockInit` |

`Take` ne prête pas une pièce absente :

```
throw new InvalidOperationException($"Stock insuffisant pour {a}.");
```

C'est un contrat, pas un avertissement. Si cette exception sort, c'est un bug de l'optimiseur, pas une donnée invalide.

`Give` existe parce que l'optimiseur se dédit. Trois endroits rendent des pièces dans `Optimizer.Optimize` :

- un palonnier dont une extrémité ne trouve aucune ligne de mother est retiré, et ses pièces rendues ;
- une ligne de mother qui n'a pas pu être constituée fait retirer tous les palonniers qui pendaient d'elle, et rendre leurs pièces ;
- une ligne de mother qui se retrouve sans rien à porter est retirée, et ses pièces rendues.

Le plafond `Math.Min(p.StockInit, n + 1)` empêche un double `Give` de créer des pièces à partir de rien.

#### Ce que voit le reste de l'application

`Snapshot()` produit une copie des lignes avec `StockInit` = ce que l'inventaire tient et `Stock` = ce qu'il reste. C'est ce qui alimente les feuilles de stock de l'export Excel. Attention : **seules les lignes en stock y figurent**, puisque `Rows` a déjà filtré les `Nombre = 0`.

Le rapport, lui, refait le compte dans l'autre sens. `BuildTrussInventory` compte les pièces effectivement dessinées et les compare au stock du catalogue. L'optimiseur ne pouvant pas dépasser, un dépassement signale forcément autre chose :

```
Le pré rigg utilise plus de pièces que l'inventaire n'en compte (colonne « Nombre ») :
S500 Carrée Acier lg:5,6m : 42 pièces pour 39 en stock.
```

```
Le pré rigg utilise des longueurs qui ne sont pas en stock dans l'inventaire (colonne
« Nombre » nulle, ou longueur absente) : S500 Carrée Acier lg:7m (2 pièce(s)).
```

Dans les deux cas la cause est la même famille de causes : un élément placé à la main, importé, ou placé avec un inventaire différent de celui qui est sur le disque aujourd'hui.

#### Quand le stock ne suffit pas

`Pipeline` explique le refus plutôt que de le laisser deviner. `LongestAssembly` calcule le plus long pont que chaque fichier pourrait former en mettant bout à bout **toutes** ses pièces d'un même modèle, plafonné à `Assembler.MaxPieces = 16`. Avec 39 pièces de 5,6 m, cela donne `16 × 5,6 = 89,6 m`. Ce chiffre est ensuite comparé à la géométrie de la salle :

```
Même assemblé de toutes ses pièces, le plus long palonnier de Palonnier.xlsx mesure 4 m,
alors que deux lignes de points sont espacées d'au moins 3,6 m.
```

```
Même assemblé de toutes ses pièces, le plus long mother de MotherGrill.xlsx mesure 5,6 m,
alors que les poutres sont espacées de 7,14 à 7,7 m.
```

Les espacements cités sont ceux des vraies coordonnées, poutre J exclue : les lignes de points 1 à 9 sont à 3,600–4,101 m les unes des autres, et les poutres I à A de 7,144 m (E–D) à 7,700 m (H–G et C–B).

Et quand un modèle existe mais ne porte pas, `MotherLines.Explain` donne le chiffre :

```
Ligne de mother G5–D5 : 4 500 kg sur la travée G5–F5 (7,25 m), alors que le modèle le plus
fort en stock y admet 4 182 kg.
```

Sources : `Shared/ScenarioHelpers/Core/Assembly.cs`, `PieceStock` en entier ; `Shared/ScenarioHelpers/Core/Optimizer.cs`, `Optimize` (les trois `Give`) ; `Shared/ScenarioHelpers/Core/MotherLines.cs`, `Explain` ; `Shared/ScenarioHelpers/Pipeline.Messages.cs`, `LongestAssembly` ; `Shared/ScenarioHelpers/Report/ReportDataBuilder.cs`, `BuildTrussInventory`.

### 5.4 Ce qu'on enregistre : la composition

Le plan finit en base de données. Que faut-il y écrire pour qu'une relecture, six mois plus tard, retrouve le même pont ?

La réponse naïve : la longueur. Elle ne suffit pas. « 22,4 m » ne dit pas si c'est 4 × 5,6 ou 8 × 2,8, et les deux n'ont ni la même masse, ni le même nombre de jonctions, ni le même effet sur le stock.

Le code enregistre donc les **pièces**, dans une colonne `Composition` de `MotherGridPalonnier`.

```
[Column("Composition")]
[StringLength(400)]
public string? Composition { get; set; }
```

Le format est du JSON, en **centimètres entiers**, trié du plus long au plus court :

```
"[560,560,560,560]"      4 pièces de 5,6 m
"[560,280]"              5,6 m + 2,8 m
```

Trois raisons à ce format, dont deux dans l'en-tête de `Shared/TrussComposition.cs` : il est indépendant de la culture (pas de virgule décimale contre point décimal), il est exempt du bruit en virgule flottante des tableurs, et il est court — les 400 caractères de la colonne (`MotherGridPalonnier.Composition`) suffisent largement aux 16 pièces maximum.

Une règle implicite mais ferme : **chaque pièce est du modèle de l'élément**, c'est-à-dire de son `ElementType`. La composition ne porte que des longueurs, jamais un modèle.

#### Écriture

`Pipeline.BuildElementEntities` transcrit le résultat de l'optimiseur en lignes de base. Pour un mother comme pour un palonnier, c'est la même ligne :

```
Composition = m.Assembly?.Composition,
```

et `Assembly.Composition` appelle `TrussComposition.Serialize`. Le nom de l'élément est construit à part :

```
Name = $"{m.Bar.Type} L={m.Bar.Length:F1}m #{i + 1}"     →  "S500 Carrée Acier L=22,4m #1"
```

La longueur du nom est celle du pont assemblé (22,4 m), pas celle de la travée dessinée (21,55 m). Les coordonnées `X1,Y1 → X2,Y2`, elles, sont celles de la travée. **Les deux ne coïncident pas, et c'est normal.**

L'import Excel des éléments écrit la même colonne, mais depuis un texte tapé par un humain. `TrussComposition.ParseText` accepte `5,6+2,8`, `5.6 + 2.8`, `2x5,6+2,8` et `2*5.6 + 2.8`, avec un `m` final optionnel. Un texte qu'il ne comprend pas n'est jamais deviné :

```
Row 'MG-12': pieces '5,6 et 2,8' unreadable — ignored.
```

#### Relecture

`ReportDataBuilder.ReadPieces` est le point de lecture de l'étude — la page MotherGrid en a un second, `TrussPieces` —, et il a trois issues :

1. Une composition lisible : ce sont les pièces, triées décroissant. `Recorded = true`.
2. Rien d'enregistré (`null`) : l'élément a été placé avant que les pièces ne soient enregistrées. On le compte comme **une seule pièce** de la longueur lue dans son nom, par l'expression régulière `L\s*=\s*(\d+(?:[.,]\d+)?)\s*m` — les deux séparateurs décimaux sont acceptés, puisque le nom a été écrit avec la culture du serveur. À défaut de longueur dans le nom, c'est la distance dessinée.
3. Quelque chose d'enregistré mais d'illisible : `Unreadable = true`, et l'élément est compté comme au point 2 — mais l'étude le dit.

```
Les pièces enregistrées de 2 élément(s) sont illisibles : PAL-7, MG-3. Chacun est compté
comme une seule pièce de la longueur portée dans son nom.
```

#### Ce que la composition sert à faire

- **Le Tableau 2 de l'étude**, la nomenclature du pré rigg. `BuildTrussInventory` développe chaque élément en ses pièces **avant** de grouper, de sorte que le tableau compte ce que l'équipe sort du camion, pièce par pièce : une ligne `S500 Carrée Acier lg:5,6m`, 4 pièces, et non une ligne « un pont de 22,4 m ». Les lignes sont groupées à la clé `(modèle, longueur au centimètre)`, puis fusionnées par étiquette — un même pont servant de mother et de palonnier n'apparaît qu'une fois, comme le font les études écrites à la main.
- **La masse**. `MassSource` additionne les masses catalogue des pièces. C'est *la même* fonction qui alimente le calcul de charpente et l'étude, de sorte que les charges enregistrées et le document ne puissent jamais être en désaccord sur le poids d'un pont.
- **La liste des ponts assemblés**, imprimée sous le Tableau 2 pour les seuls éléments de plus d'une pièce, avec `TrussComposition.Describe` : `5,6 + 2,8 m`, ou `3 × 5,6 + 2,8 m` quand une longueur se répète.
- **Un contrôle de cohérence.** Si les pièces enregistrées sont plus courtes que la longueur sur laquelle l'élément est dessiné, de plus de `PieceReachToleranceM = Geometry.TOL + 0,02 = 0,10 m`, c'est que la fiche décrit un pont qui ne peut pas être là :

```
Les pièces de certains ponts sont plus courtes que la longueur sur laquelle ils sont
dessinés : « MG-4 » (2 × 5,6 m pour 21,55 m).
```

Le contrôle ne se déclenche que dans ce sens. Un pont **plus long** que la travée est normal : c'est le cas ordinaire, 22,40 m assemblés pour 21,55 m dessinés.

> Ce que la composition ne dit pas, et ne dira pas : où sont les jonctions. Elle donne un multi-ensemble de longueurs, pas un ordre ni des positions. Toute la vérification du chapitre 4 repose sur l'hypothèse que cela n'a pas d'importance.

Sources : `Shared/TrussComposition.cs` (`Serialize`, `Parse`, `ParseText`, `Describe`) ; `Shared/Data/Models/MotherGridPalonnier.cs`, la propriété `Composition` ; `Shared/ScenarioHelpers/Pipeline.Entities.cs`, `BuildElementEntities` ; `Shared/ScenarioHelpers/Pipeline.Imports.cs`, l'import des éléments ; `Shared/ScenarioHelpers/Report/ReportDataBuilder.cs`, `ReadPieces`, `PiecesOf`, `ReferenceLengthOf`, `BuildTrussInventory`, `MassSource` ; `Shared/ScenarioHelpers/Report/WordReportBuilder.cs`, le bloc du Tableau 2.

## Chapitre 6 — Les charges qui n'ont besoin de personne

### 6.1 Une charge sur une ligne de points

Une production arrive avec un plan de charges : une liste de points, chacun avec une position et un poids. Le problème que l'optimiseur doit résoudre est toujours le même : amener chaque charge jusqu'à la charpente. La réponse naïve est de suspendre chaque charge à un palonnier, et de suspendre chaque palonnier à deux lignes de mother. Elle est fausse, et pour trois raisons qui se voient tout de suite sur un plan réel.

La première : un palonnier coûte une pièce de treillis, prise dans un stock fini. La deuxième : un palonnier pèse. Ses 47 kg redescendent sur les lignes de mother qui le portent, et l'étude les compte comme une charge parmi les autres. La troisième, la plus importante : ce n'est pas ce que font les études écrites à la main. Dans ces études, la plupart des moteurs pendent directement de la ligne de mother, sans rien entre eux et elle.

D'où la première règle de l'optimiseur, appliquée avant tout le reste :

**Une charge posée sur une ligne de points pend directement de la ligne de mother de cette ligne de points.**

Le code est court. Pour chaque charge, il cherche la ligne de points la plus proche en y, et compare l'écart à une tolérance.

```
var (row, ry) = NearestRow(c.Y);
if (Math.Abs(c.Y - ry) <= TOL) direct.Add((c, row, ry));
else                           toHang.Add(c);
```

`TOL` vaut **0,08 m**, soit 8 cm. C'est `Geometry.TOL`, déclarée `// tolérance position (m)`. La constante ne porte aucune justification dans le code : elle n'est pas dérivée d'un diamètre d'élingue ni d'une précision de relevé, c'est un nombre choisi.

`NearestRow` compare aux neuf lignes de points relevées sur site. Elles ne sont pas régulières :

| Ligne de points | y de la grille (m) | X d'étude (m) | Écart à la ligne précédente (m) |
|---|---|---|---|
| 1 | 15,150 | −15,150 | — |
| 2 | 11,050 | −11,050 | 4,100 |
| 3 | 7,400 | −7,400 | 3,650 |
| 4 | 3,700 | −3,700 | 3,700 |
| 5 | 0,000 | 0,000 | 3,700 |
| 6 | −3,700 | 3,700 | 3,700 |
| 7 | −7,450 | 7,450 | 3,750 |
| 8 | −11,050 | 11,050 | 3,600 |
| 9 | −15,151 | 15,151 | 4,101 |

Les écarts vont de 3,600 m à 4,101 m. L'origine est E5 : la ligne 5 est à y = 0, et E5 se trouve à 33,103 m de J5 le long des poutres. La colonne « X d'étude » est ce qu'une personne lit et tape sur la page Charges, car les axes de l'étude et ceux de la grille sont symétriques : `StudyAxes` convertit `(x, y) -> (-y, -x)`, et cette conversion est sa propre réciproque.

#### Le plan du propriétaire, charge par charge

Le plan de la salle, celui du dossier partagé `_common/data`, contient 24 charges, 6 210 kg au total. Prenons la première ligne du fichier :

```
L1 | -7,45 | -2,99 | 154
```

X d'étude = −7,45 m, Y d'étude = −2,99 m. En coordonnées de grille : x = −(−2,99) = **2,99 m**, y = −(−7,45) = **7,45 m**. La ligne de points la plus proche de y = 7,45 est la ligne 3, à 7,400 m. L'écart vaut 7,45 − 7,400 = **0,05 m**. 0,05 ≤ 0,08, donc L1 pend directement de la ligne de mother de la ligne 3.

Ce n'est pas un hasard isolé. Les 24 charges se répartissent ainsi :

| Groupe de charges | y de grille (m) | Ligne la plus proche | Écart (m) | Verdict |
|---|---|---|---|---|
| L1, L4, L7, L10, L13, L18 | 7,45 | 3 (7,400) | 0,05 | directe |
| L2, L5, L8, L11, L14, L19 | 0,00 | 5 (0,000) | 0,00 | directe |
| L3, L6, L9, L12, L15, L20 | −7,40 | 7 (−7,450) | 0,05 | directe |
| L16 | 1,02 | 5 (0,000) | 1,02 | palonnier |
| L17 | −5,44 | 6 (−3,700) | 1,74 | palonnier |
| S1, S2 | 9,00 | 3 (7,400) | 1,60 | palonnier |
| S3, S4 | −9,00 | 7 (−7,450) | 1,55 | palonnier |

**18 charges sur 24 partent directement**, soit 3 926 kg des 6 210 kg. Six charges seulement, 2 284 kg, iront sur des palonniers. En mode verbeux l'optimiseur l'annonce littéralement :

```
  18 charge(s) sur une ligne de points, 6 sur palonnier
```

Les 5 cm d'écart des lignes L1 et L3 méritent qu'on s'y arrête. Le plan a été dessiné à +7,45 m d'un côté et −7,40 m de l'autre, la charpente relevée à +7,400 m et −7,450 m : le plan et la charpente sont décalés en sens inverse, d'où les 5 cm d'écart des deux côtés. Sans la tolérance de 8 cm, douze charges de plus tomberaient du côté « palonnier ». Le seuil est donc chargé de rattraper un écart de plan, pas seulement un jeu d'accroche.

#### Ce qui se passe de l'autre côté du seuil

Une charge à 0,09 m d'une ligne de points n'est pas « presque directe » : elle bascule entièrement dans l'autre monde. Elle devient candidate à un palonnier, et ce palonnier ne s'arrête pas à la charge : il court d'une ligne de points à la suivante, parce qu'un palonnier doit pendre de deux lignes de mother.

Exemple à vérifier de tête. Une charge à y = −3,60 m. La ligne 6 est à −3,700 m ; l'écart vaut 0,10 m > 0,08 m, donc palonnier. `Geometry.EnclosingRows(−3,60 ; −3,60)` cherche alors la ligne immédiatement en dessous (la plus haute des lignes dont le y est ≤ −3,60 + 0,08 = −3,52 : c'est la ligne 6, à −3,700) et la ligne immédiatement au-dessus (la première dont le y est ≥ −3,60 − 0,08 = −3,68 : c'est la ligne 5, à 0,000). Portée du palonnier : 0,000 − (−3,700) = **3,700 m**.

Dix centimètres d'écart coûtent donc un palonnier de 3,70 m, ses deux pièces de treillis, et son poids propre sur deux lignes de mother. À 0,08 m, il ne coûte rien.

> Le seuil ne regarde que le y. Il n'existe aucune règle symétrique pour le x : une charge posée exactement sur une poutre n'échappe pas pour autant à la ligne de mother, elle y pend comme les autres. Et le seuil se mesure contre les neuf lignes de points relevées, jamais contre la position réelle de la ligne de mother, qui n'est pas encore décidée à ce stade.

Source : `Shared/ScenarioHelpers/Core/Optimizer.cs`, `Optimize` (étape 1), et `Shared/ScenarioHelpers/Core/Geometry.cs`, `NearestRow` et `EnclosingRows`.

### 6.2 Ce que cela change pour la suite

Une charge directe ne disparaît pas : elle change de statut. Trois choses lui arrivent, dans cet ordre.

1. Elle devient une **demande** sur sa ligne de points. L'étape 3 construit la liste `demands` à partir des extrémités des palonniers et des charges directes, chacune sous la forme `(ligne, y, x)`. `MotherLines.Plan` en déduit quelles travées doivent être couvertes par une ligne de mother.
2. Elle est **rattachée** à la ligne de mother qui la couvre : `MotherLines.LineAt(lines, d.Row, d.Charge.X)`, puis `line.DirectCharges.Add(d.Charge)`. Si aucune ligne ne la couvre, l'optimiseur enregistre l'erreur `« {nom} : aucune ligne de mother à sa position. »` — un garde-fou, puisque la ligne a justement été planifiée à partir de cette demande.
3. Elle devient un **point de suspension** sur cette ligne : `new SuspensionPoint { X = OnLine(line, c.X), F = c.Weight, W = 0 }`.

Le `W = 0` est le gain visible : une charge directe n'apporte aucun poids propre de palonnier. Un palonnier, lui, apporte le sien, et ce poids compte dans la charge de chaque travée de mother.

Le `OnLine(line, c.X)` est un `Math.Clamp(x, line.XL, line.XR)` : un point d'accroche placé quelques centimètres au-delà de l'extrémité dessinée de la ligne est ramené sur cette extrémité, et sa charge passe alors directement dans la poutre. C'est exactement ce que fait le calcul charpente, donc l'optimiseur dimensionne chaque travée pour ce que le calcul y trouvera.

#### Le gain combinatoire, en chiffres

C'est le vrai effet de l'étape 1, et il est massif. Le générateur de groupes du chapitre 7 ne voit que `toHang`. Sur le plan du propriétaire :

| | Charges vues par le générateur | Combinaisons Y examinées | Combinaisons biais examinées | Total |
|---|---|---|---|---|
| Avec la règle des charges directes | 6 | 9 | 50 | **59** |
| Sans elle (24 charges) | 24 | 51 | 12 926 | **12 977** |

Le détail du calcul « sans elle » : les 24 charges se répartissent en neuf paquets de même x — six paquets de 3 charges et trois paquets de 2 — soit 6 × (2³ − 1) + 3 × (2² − 1) = 42 + 9 = 51 combinaisons Y ; et les groupes biais examinent toutes les parties de taille 2 à 4, soit C(24,2) + C(24,3) + C(24,4) = 276 + 2 024 + 10 626 = 12 926. Facteur 220 environ.

#### Les trois charges d'un même x : l'exemple à ne pas se tromper

Le plan contient L1, L2 et L3, toutes trois au même x de grille (2,99 m) et sur trois lignes de points différentes : 3, 5 et 7. Un lecteur pressé imagine un beau palonnier vertical portant les trois. Il n'existe pas, et pour deux raisons cumulées.

D'abord, elles sont toutes les trois directes : elles ne sont jamais présentées au générateur de groupes. Ensuite, même si elles l'étaient, aucun de leurs groupements ne survivrait :

| Groupe | y min / y max | Lignes encadrantes | Portée (m) | Charge (kg) |
|---|---|---|---|---|
| L1 + L2 | 0,00 / 7,45 | 5 → 3 | 7,400 | 498 |
| L2 + L3 | −7,40 / 0,00 | 7 → 5 | 7,450 | 498 |
| L1 + L3 | −7,40 / 7,45 | 7 → 3 | 14,850 | 308 |
| L1 + L2 + L3 | −7,40 / 7,45 | 7 → 3 | 14,850 | 652 |

Avec l'inventaire `Palonnier.xlsx` livré, aucun modèle n'a de valeur admissible au-delà de **6,01 m** (deux fois la plus grande longueur en stock, 3 m, plus la tolérance de longueur de 1 cm). Les quatre groupes sont donc refusés par le filtre de faisabilité du § 7.3, et les trois charges se retrouveraient seules de toute façon.

Il y a un troisième effet, plus subtil. Si L1 arrivait seule au générateur, `EnclosingRows(7,45 ; 7,45)` trouverait la même ligne des deux côtés : en dessous la ligne 3 (7,400 ≤ 7,45 + 0,08) et au-dessus la ligne 3 aussi (7,400 ≥ 7,45 − 0,08). Le code a un garde-fou pour ce cas — il prend alors la ligne suivante — et le résultat serait un palonnier de la ligne 3 à la ligne 2, soit 11,050 − 7,400 = **3,650 m**, avec la charge à 5 cm d'une de ses extrémités. Absurde, et jamais construit : la règle des charges directes l'écarte avant.

> Une charge directe n'est plus jamais reconsidérée. L'optimiseur ne revient pas en arrière pour la mettre sur un palonnier, même si cela soulageait une travée de mother surchargée. La règle du § 6.1 est définitive.

Source : `Shared/ScenarioHelpers/Core/Optimizer.cs`, `Optimize` (étape 1), `Build` (étapes 3 et 4) et `OnLine` ; `Shared/ScenarioHelpers/Core/MotherLines.cs`, `Plan` et `LineAt`.

## Chapitre 7 — Grouper les charges sous un palonnier

### 7.1 Les groupes le long des poutres

Les charges qui restent après le chapitre 6 doivent aller sur un palonnier. Un palonnier, ici, est une barre suspendue par ses deux extrémités à deux lignes de mother, donc à deux lignes de points. Il court d'une ligne de points à une autre — c'est-à-dire, dans la grille, parallèlement aux poutres.

La question de l'optimiseur n'est pas « où mettre un palonnier ». C'est : **quels sous-ensembles de charges un même palonnier pourrait-il porter ?** Il les énumère tous, puis choisit (§ 7.4). Cette section décrit la première famille, les groupes dits **Y** : des charges qui partagent le même x.

#### Les paquets de x

`GenerateYGroups` range d'abord les charges par x voisin :

```
foreach charge :
    chercher un paquet dont la clé kx vérifie |x - kx| <= TOL   (TOL = 0,08 m)
    si trouvé  : y ajouter la charge
    sinon      : créer un paquet dont la clé est le x de CETTE charge
```

Trois choses à savoir sur ce regroupement, qu'un lecteur suppose souvent à tort.

- La clé d'un paquet est le x de **la première** charge qui l'a créé, pas la moyenne du paquet. Le palonnier sera dessiné à ce x-là.
- Le regroupement n'est **pas transitif**. Trois charges à x = 0,00, 0,07 et 0,14 donnent deux paquets : 0,00 et 0,07 sont ensemble (écart 0,07 ≤ 0,08), 0,14 est seule (écart 0,14 à la clé 0,00).
- L'ordre des charges décide donc des paquets. C'est une des raisons pour lesquelles l'ordre du plan est figé (§ 7.4).

#### Toutes les parties d'un paquet

Pour un paquet de k charges, le code énumère **toutes** les parties non vides, de la taille 1 à la taille k :

```
nombre de groupes Y d'un paquet de k charges = 2^k - 1
nombre de groupes Y d'un plan               = Σ (2^k - 1)  sur tous les paquets
```

Il n'y a **aucun plafond de taille** sur les groupes Y, contrairement aux groupes biais (§ 7.2). Vingt charges au même x produiraient 2²⁰ − 1 = 1 048 575 groupes candidats. Sur le plan du propriétaire les paquets font 2 charges, donc 3 groupes chacun, 9 en tout.

La taille 1 est incluse, et c'est important : c'est le repli d'une charge que rien ne peut accompagner. Sur le plan du propriétaire, les six palonniers finalement posés sont tous des groupes de taille 1.

#### Les deux extrémités

Pour chaque partie, le code prend le y le plus bas et le y le plus haut des charges, puis demande les lignes encadrantes :

```
var (below, above) = EnclosingRows(yMin, yMax);
A = (xKey, below.Y);
B = (xKey, above.Y);
Span = above.Y - below.Y;
```

`EnclosingRows` prend la tolérance en compte des deux côtés : `below` est la ligne la plus haute dont le y est ≤ yMin + 0,08, `above` la première dont le y est ≥ yMax − 0,08. Conséquence directe, et contre-intuitive : **une paire de charges qui enjambe une ligne de points de plus de 8 cm des deux côtés saute aux lignes suivantes.**

Exemple à vérifier. Deux charges au même x, à y = 3,40 et y = 4,00 — 60 cm l'une de l'autre, de part et d'autre de la ligne 4 (3,700). `below` : la plus haute ligne dont le y est ≤ 3,40 + 0,08 = 3,48 ; la ligne 4 est à 3,700 > 3,48, donc c'est la ligne 5, à 0,000. `above` : la première ligne dont le y est ≥ 4,00 − 0,08 = 3,92 ; la ligne 4 est à 3,700 < 3,92, donc c'est la ligne 3, à 7,400. Portée : **7,400 m** pour deux charges distantes de 60 cm. Avec l'inventaire livré, ce groupe est refusé (au-delà de 6,01 m), et chaque charge repart seule sur un palonnier de 3,700 m.

#### Ce que le groupe Y ne dit pas

Le palonnier est dessiné à `xKey`. Une charge du paquet peut être à 8 cm de là. Cet écart n'est corrigé nulle part : quand `Reactions2Pt` et `SupportReactions` calculent ce que porte chaque extrémité, ils projettent la charge sur l'axe A→B (`Geometry.ProjOnAxis`), et cet axe est vertical pour un groupe Y. Les 8 cm de décalage latéral sont donc simplement effacés. Il n'y a ni moment de torsion, ni vérification que la charge est bien sous la barre.

Source : `Shared/ScenarioHelpers/Core/GroupGenerator.cs`, `GenerateYGroups` et `Combinations` ; `Shared/ScenarioHelpers/Core/Geometry.cs`, `EnclosingRows` et `ProjOnAxis`.

### 7.2 Les groupes inclinés, et la colinéarité

Certaines charges ne partagent aucun x et ne sont pas sur une ligne de points. Un palonnier incliné — un **biais** — peut les prendre ensemble, à condition qu'elles soient alignées. `GenerateBiaisGroups` s'en charge, sauf si le réglage *orientations orthogonales uniquement* est actif (`OptimizerOptions.OrthogonalOnly`), auquel cas aucun groupe biais n'est produit — c'est le mode qui reproduit les études écrites à la main.

#### Les deux refus d'entrée

Pour chaque partie de 2 à 4 charges, deux tests avant tout le reste :

```
if (dxTot < TOL) continue;   // même x  → déjà traité en Y
if (dyTot < TOL) continue;   // même y  → ignoré
```

`dxTot` et `dyTot` sont les étendues du groupe en x et en y. Le premier refus est une déduplication : ces charges forment déjà un groupe Y. Le second est une **limite réelle de l'optimiseur** : des charges alignées en y, c'est-à-dire alignées en travers des poutres, ne seront jamais groupées. Sur le plan du propriétaire, S1 (x = 1, y = 9) et S2 (x = 2, y = 9) sont à 1 m l'une de l'autre sur la même parallèle ; elles auront chacune leur palonnier. La raison est structurelle : un palonnier parallèle aux lignes de mother ne pourrait pendre que d'une seule ligne, et l'optimiseur n'en construit aucun qui ne pende de deux.

#### L'axe contre lequel la colinéarité se mesure

Pour trois charges ou plus, il faut vérifier qu'elles sont alignées. Alignées par rapport à quoi ? C'est toute la question, et c'est là que le code a été corrigé.

L'axe retenu est celui **de la charge la plus basse en y à la charge la plus haute en y** :

```
var cLow  = sub.MinBy(c => c.Y)!;
var cHigh = sub.MaxBy(c => c.Y)!;
if (size >= 3 && !WithinAxis(sub, cLow, cHigh)) continue;
```

Ce n'est pas un détail de style. C'est **exactement** l'axe sur lequel le palonnier sera construit quelques lignes plus bas : `dirX = cHigh.X - cLow.X`, `dirY = cHigh.Y - cLow.Y`, puis A et B sont les intersections de cette droite avec les deux lignes de points encadrantes. Mesurer la colinéarité contre une autre droite — par exemple celle qui joint les deux charges les plus écartées en x — revient à vérifier un alignement qui ne sera pas celui du treillis posé.

`WithinAxis` calcule une distance perpendiculaire par produit vectoriel :

```
cross = |ax·(c.Y - from.Y) - ay·(c.X - from.X)| / ||(ax, ay)||
rejet si cross > ColinearTol      (ColinearTol = 0,20 m)
```

Le numérateur `|ax·Δy − ay·Δx|` est le double de l'aire du triangle formé par les trois charges. Il ne dépend **pas** de la droite choisie : seul le dénominateur change. Choisir une base courte, c'est diviser la même aire par un petit nombre, et obtenir une hauteur énorme.

#### Le chiffre, mis à plat

Trois charges, dans la travée entre les lignes 5 (y = 0) et 4 (y = 3,700) :

```
C1 = (-8,00 ; 2,00)
C2 = (-0,10 ; 2,10)
C3 = ( 0,00 ; 1,90)

double aire du triangle = 1,590 m²   (le même quelle que soit la base)

base « extrêmes en x » : C1 -> C3    longueur 8,0006 m
   écart de C2         : 1,590 / 8,0006 = 0,199 m   <= 0,20  -> accepté

base réelle du palonnier : C3 -> C2  longueur 0,2236 m   (cLow -> cHigh)
   écart de C1           : 1,590 / 0,2236 = 7,111 m
```

Mesuré contre les extrêmes en x, le groupe passe tout juste. Mesuré contre l'axe que l'on va réellement construire, une charge est à **7,11 m** du treillis. Et le palonnier correspondant est parfaitement constructible : la droite C3→C2 coupe la ligne 5 en x = 0,95 et la ligne 4 en x = −0,90, ce qui donne une portée de 4,14 m, une longueur que l'inventaire sait assembler. Le filtre de faisabilité ne l'aurait donc pas sauvé. C'est ce mécanisme qui, dans un essai aléatoire, a laissé une charge à 2,24 m de son palonnier.

> La tolérance de colinéarité, 0,20 m, est vingt fois plus large que la tolérance de position de la mother sur une poutre (10 mm) et deux fois et demie plus large que la tolérance de position générale (0,08 m). Une charge peut donc pendre à 20 cm de côté du treillis qui la porte, et le calcul charpente l'accepte : `ReportDataBuilder.AcrossPalonnierToleranceM` vaut `ColinearTol + 0,01`, soit 0,21 m, précisément pour rester cohérent avec ce que l'optimiseur a admis.

#### Le prolongement jusqu'aux lignes de points

Une fois l'axe validé, le palonnier n'est pas la corde entre les charges extrêmes : il est prolongé jusqu'aux deux lignes de points encadrantes.

```
xA = cLow.X + (below.Y - cLow.Y) / dirY * dirX
xB = cLow.X + (above.Y - cLow.Y) / dirY * dirX
A  = (arrondi(xA, 4 décimales), below.Y)
B  = (arrondi(xB, 4 décimales), above.Y)
Span = ||B - A||
```

C'est ici que les groupes biais deviennent dangereux, et c'est ici que le filtre du § 7.3 gagne son salaire. Plus l'alignement est plat, plus le prolongement est long. Deux charges à 5,00 m l'une de l'autre en x et 0,20 m en y, entre les lignes 5 et 4, donnent un facteur de prolongement de 3,700 / 0,20 = 18,5 : le palonnier mesurerait plus de 90 m. Sur le plan du propriétaire, les dix paires biais examinées ont des portées de **17,69 m à 23,83 m**, et sont toutes refusées.

Aucune borne géométrique ne limite xA ou xB : rien n'interdit à un palonnier candidat de sortir de la salle. Seule la faisabilité l'élimine.

#### Le plafond de taille

```
int maxSize = Math.Min(4, n);
for (int size = 2; size <= maxSize; size++)
```

Les groupes biais sont limités à **4 charges**. Sans ce plafond, il faudrait tester toutes les parties de taille ≥ 2, soit 2ⁿ − n − 1 combinaisons : 16 777 191 pour 24 charges. Avec lui, le compte est polynomial :

```
combinaisons biais examinées = C(n,2) + C(n,3) + C(n,4)
n = 6   ->   15 +   20 +     15 =      50
n = 24  ->  276 + 2 024 + 10 626 =  12 926
```

Le prix de ce plafond est honnête à dire : un alignement réel de cinq charges ne sera jamais porté par un seul palonnier. Il sera coupé en deux au mieux.

Sur le plan du propriétaire, les 50 combinaisons biais se répartissent ainsi : 3 refusées pour `dxTot < TOL` (les paires L16+L17, S1+S3, S2+S4), 2 refusées pour `dyTot < TOL` (S1+S2 et S3+S4), 35 refusées pour colinéarité (tous les triplets et tous les quadruplets), 10 arrivées jusqu'au filtre de faisabilité — et refusées là. L'optimiseur l'affiche :

```
  → 6 groupes Y, 0 groupes biais
```

Source : `Shared/ScenarioHelpers/Core/GroupGenerator.cs`, `GenerateBiaisGroups`, `WithinAxis` et `MakeKey` ; `Shared/ScenarioHelpers/Core/Geometry.cs`, `EnclosingRows`.

### 7.3 Le filtre de faisabilité

Un groupe candidat n'est pas ajouté à la liste s'il est impossible à construire. Le test est passé au générateur sous forme de fonction, ce qui est le point de conception important : la faisabilité **élague**, elle ne trie pas après coup.

```
var allGroups = GroupGenerator.Generate(toHang, Feasible, allowBiais: !options.OrthogonalOnly);
```

`Feasible(span, load)` est défini dans `Optimize` :

```
var key = ((long)Math.Round(span * 100), (long)Math.Ceiling(load - 1e-9));
if (!feasible.TryGetValue(key, out var ok))
    feasible[key] = ok = AssemblyFor(palStock, palTable, span, key.Item2) is not null;
```

Un seul de ces arrondis va dans le sens sûr : la charge est arrondie vers le haut au kilogramme (1 250,4 kg devient 1 251 kg), et c'est elle qui est passée à `AssemblyFor`, si bien qu'un groupe n'est jamais dimensionné pour moins que ce qu'il porte. La portée, elle, n'est arrondie au centimètre (au plus proche, pas vers le haut) que pour servir de clé de mémorisation : le dimensionnement lui-même se fait toujours à la portée exacte.

`AssemblyFor` fait le travail :

```
public static Assembly? AssemblyFor(PieceStock stock, LoadTable table, double span, double load) =>
    stock.Best(Math.Max(0.01, span - TOL), model => table.At(model, span).Carries(load));
```

Il demande au stock le meilleur assemblage d'**un seul modèle**, atteignant `span − 0,08 m`, parmi les modèles dont la valeur d'abaque à la portée `span` porte la charge. Trois règles s'y cachent :

- Une pièce 8 cm plus courte que la portée couvre quand même la portée.
- La **longueur assemblée n'entre pas** dans la capacité. Un assemblage de 4,20 m posé sur une portée de 3,70 m est crédité de la valeur du modèle **à 3,70 m**, pas à 4,20 m. Où tombent les jonctions n'est pris en compte nulle part.
- `Carries(kg)` accepte `kg <= admissible + 1e-6`. L'égalité passe.

#### D'où vient le refus, la plupart du temps

`LoadTable.At(model, span)` lit la *charge ponctuelle au milieu* du modèle à la longueur immédiatement supérieure à la portée (avec 1 cm de tolérance de longueur). Au-delà de la plus grande ligne du modèle, la valeur est **calculée à moment égal** :

```
p = poids propre de la plus longue ligne / sa longueur   (kg/m)
M = P0·L0/4 + p·L0²/8
P(L) = 4·(M - p·L²/8) / L
```

et seulement jusqu'à `ExtrapolationLimit = 2,0` fois cette longueur. Au-delà, pas de valeur du tout, avec la raison en français : `« portée de plus de 2 fois la plus grande longueur de l'inventaire (3 m) »`.

Avec le `Palonnier.xlsx` livré, la plus longue pièce de palonnier fait **3 m**. Le mur est donc à **6,01 m**. Voici ce que l'abaque donne, par modèle :

| Modèle | Plus longue ligne | Charge ponctuelle au milieu | Valeur à 3,70 m | Portée maximale |
|---|---|---|---|---|
| ALC500 | 3,0 m | 5 793 kg | 4 690 kg | 6,01 m |
| F34200 | 2,0 m | 3 000 kg | 1 605 kg | 4,01 m |
| F34300 | 3,0 m | 2 000 kg | 1 614 kg | 6,01 m |
| S500 Carrée Alu | 3,0 m | 2 000 kg | 1 614 kg | 6,01 m |
| S500 Tri Alu | 2,8 m | 2 031 kg | 1 528 kg | 5,61 m |
| SC500 | 2,8 m | 2 860 kg | 2 156 kg | 5,61 m |

Tout groupe de portée supérieure à 6,01 m est donc mort-né, sans qu'on ait jamais cherché des pièces. C'est ce qui élimine, sur le plan du propriétaire, les dix paires biais (17,69 m à 23,83 m), la paire L16+L17 (11,15 m) et les paires S1+S3 et S2+S4 (22,10 m). Sur 59 combinaisons examinées, **6 groupes seulement** survivent — les six charges seules.

#### Ce que la mémorisation économise vraiment

Deux dictionnaires de mémorisation existent, et ils ne servent pas la même chose.

| Mémo | Où | Clé | Durée de vie | État du stock |
|---|---|---|---|---|
| `feasible` | `Optimize` | (portée en cm, charge en kg) | toute la génération | stock **complet**, jamais mis à jour |
| `fits` | `SelectGreedy` | (portée en cm, charge en kg) | **un tour** de la boucle gloutonne | stock restant à ce tour |

Le premier est donc **optimiste** : il répond « avec l'inventaire entier, cela se construirait ». Le second est refait à zéro à chaque tour, parce que le tour précédent a consommé des pièces.

Le gain mesuré sur ce plan est modeste, et il faut le dire. Sur les 19 interrogations qui atteignent `feasible`, une seule retombe sur une clé déjà connue : les biais S1+S4 et S2+S3, symétriques l'un de l'autre, ont la même portée au centimètre (22,13 m) et la même charge (900 kg). La mémorisation paie quand un plan répète les mêmes portées **et** les mêmes charges — un pré-rigg de moteurs identiques, typiquement. Le vrai gain du § 7.3 n'est pas le mémo, c'est l'élagage : 53 des 59 combinaisons ne deviennent jamais des groupes, et le choix glouton du § 7.4 ne les voit donc jamais.

Source : `Shared/ScenarioHelpers/Core/Optimizer.cs`, `Optimize` (fonction locale `Feasible`) et `AssemblyFor` ; `Shared/ScenarioHelpers/LoadTable.cs`, `At` ; `Shared/ScenarioHelpers/Core/Assembly.cs`, `PieceStock.Best` et `Assembler.Cheapest`.

### 7.4 Le choix glouton, et pourquoi il est glouton

À ce stade l'optimiseur a une liste de groupes possibles, qui se recouvrent. Il lui faut en choisir un ensemble qui couvre toutes les charges. C'est un problème de couverture d'ensembles, et il est NP-difficile. L'optimiseur ne le résout pas : il applique une heuristique gloutonne.

#### L'ordre, fixé une fois pour toutes

```
var candidates = allGroups
    .OrderByDescending(g => g.Names.Count)
    .ThenBy(g => g.Span)
    .ToList();
```

Le plus de charges d'abord, la plus petite portée ensuite. Le tri de LINQ est stable, donc les groupes restés à égalité gardent leur ordre de génération. Le tri ne dit **plus** rien du biais : il portait pour cela un `+0,1` qui ne pouvait jamais changer un résultat, et il a été retiré. La préférence entre deux candidats est écrite à un seul endroit, `PreferGroup`, un peu plus bas.

#### Chaque tour, un seul palonnier

```
while (uncovered.Count > 0)
{
    var fits = new Dictionary<(long Cm, long Kg), Assembly?>();   // refait à chaque tour
    ...
    foreach (var g in candidates)
    {
        if (!g.Names.IsSubsetOf(uncovered)) continue;
        if (best is not null && PreferGroup(g, best) >= 0) continue;
        var a = Fit(g);
        if (a is null) continue;
        (best, bestAssembly) = (g, a);
    }
    if (best is null) break;
    stock.Take(bestAssembly!);
    ...
}
```

`PreferGroup` est la préférence, en trois lignes et dans cet ordre :

```
1. le groupe qui porte le PLUS de charges          — un palonnier au lieu de deux
2. à nombre égal, le palonnier INCLINÉ             — il rassemble ce qu'aucun groupe Y n'atteint
3. à égalité encore, la PLUS COURTE portée         — moins de treillis sorti, plus de charge admise
```

Aucun nombre ajusté à la main n'y subsiste : le `+0,3` que le score ajoutait à un groupe incliné et le `+0,1` du tri ont été remplacés par cette comparaison, qui exprime la même préférence. **Le comportement est inchangé** — c'est la condition qui a permis la substitution.

La comparaison est **stricte** (`>= 0` fait passer au suivant), ce qui donne la règle à retenir :

**Chaque tour retient le premier groupe rencontré de la meilleure classe — nombre de charges, puis type — dont toutes les charges sont encore libres et que le stock restant sait assembler. Aucun égal rencontré plus tard ne le déloge, et comme le tri fait croître les portées à l'intérieur d'une classe, ce premier est aussi celui de plus courte portée.**

Le prix payé par tour est donc un balayage de la liste avec un test d'inclusion bon marché, plus quelques appels à `Fit` : ceux des groupes strictement préférés au meilleur en main, gagnant compris.

Un détail à connaître : `if (best is null) break;` — lorsque plus rien ne peut être construit, la boucle s'arrête en silence. Les charges restantes deviennent des `Uncovered`, l'optimiseur ajoute pour chacune `« {nom} ({poids} kg) n'est portée ni par un palonnier ni par un mother. »`, et `Pipeline.GenerateLayout` refuse d'enregistrer l'implantation.

> `PreferGroup` classe des **candidats**, pas des implantations finies. Il ne bouge donc pas avec le classement qu'un développeur enregistre (§ 11.3) : un groupe candidat n'a ni masse suspendue, ni pièces assemblées, ni travée chargée — il n'y a rien là-dedans qu'un classement réordonné puisse lire. Ce qui honore cet ordre, c'est la comparaison entre implantations terminées, et c'est la recherche exacte du chapitre 16 qui s'en sert.

#### Le plan du propriétaire, tour par tour

Les six groupes survivants sont tous de taille 1 et de type Y. Le tri les met dans l'ordre des portées croissantes :

| Tour | Groupe retenu | Portée (m) | Assemblage pris | Longueur | Valeur admissible |
|---|---|---|---|---|---|
| 1 | S3 (550 kg) | 3,600 | F34200 2,0 + 2,0 | 4,00 m | 1 651 kg |
| 2 | S4 (350 kg) | 3,600 | F34200 2,0 + 2,0 | 4,00 m | 1 651 kg |
| 3 | S1 (550 kg) | 3,650 | F34200 2,0 + 2,0 | 4,00 m | 1 628 kg |
| 4 | S2 (350 kg) | 3,650 | F34200 2,0 + 2,0 | 4,00 m | 1 628 kg |
| 5 | L16 (246 kg) | 3,700 | S500 Tri Alu 2,8 + 1,4 | 4,20 m | 1 528 kg |
| 6 | L17 (238 kg) | 3,750 | S500 Tri Alu 2,8 + 1,4 | 4,20 m | 1 507 kg |

Le changement de modèle au tour 5 est la consommation du stock en action : `Palonnier.xlsx` contient 9 pièces F34200 de 2 m. Les quatre premiers palonniers en prennent 8. Au tour 5 il n'en reste qu'une, qui ne couvre pas 3,700 − 0,08 = 3,62 m à elle seule, et F34200 disparaît des candidats. `PieceStock.Best` compare alors les autres modèles sur *le moins de pièces, puis la plus petite longueur totale, puis la plus légère* : S500 Tri Alu 2,8 + 1,4 = 4,20 m (47 kg) bat ALC500 3,0 + 2,0 = 5,00 m (60 kg) et S500 Carrée Alu 2,8 + 2,8 = 5,60 m (62 kg).

#### Glouton n'est pas optimal : un cas

Quatre charges de 300 kg formant un rectangle, dans la travée entre les lignes 5 et 4 :

```
c2 (0,00 ; 3,00)        c4 (1,00 ; 3,00)
c1 (0,00 ; 1,50)        c3 (1,00 ; 1,50)
```

Les groupes possibles de taille 2 : deux groupes **Y** — {c1,c2} à x = 0 et {c3,c4} à x = 1 — chacun de portée 3,700 m ; et deux groupes **biais** — {c1,c4} et {c2,c3} — chacun de portée 4,447 m (la droite c1→c4 coupe la ligne 5 en x = −1,000 et la ligne 4 en x = 1,467, d'où √(2,467² + 3,700²) = 4,447 m). Aucun groupe de taille 3 ou 4 ne passe la colinéarité.

La préférence met les biais avant les Y à effectif égal (`PreferGroup`, règle 2). Le glouton prend donc {c1,c4}, puis {c2,c3}. Résultat : deux palonniers de 4,45 m de portée, 10,60 m de treillis, 122 kg de poids propre suspendu aux mothers. La solution évidente — {c1,c2} et {c3,c4} — donne deux palonniers de 3,70 m, 8,00 m de treillis, 100 kg. Même nombre de palonniers, plus de matériel, plus de poids mort, et une valeur admissible plus faible : le second palonnier biais, un S500 Carrée Alu 2,8 + 2,8, n'est crédité que de 1 335 kg à 4,447 m, là où chacun des deux palonniers Y, assemblés en F34200 2,0 + 2,0, est crédité de 1 605 kg à 3,700 m.

Et si le stock ne contient qu'un seul assemblage capable d'atteindre 4,447 − 0,08 = 4,37 m, le glouton fait strictement pire : il prend {c1,c4}, puis {c2,c3} devient infaisable faute de pièces, et c2 et c3 repartent chacune sur son propre palonnier. **Trois palonniers là où deux suffisaient.** C'est la signature du glouton : un choix local irréversible, car `stock.Take` est immédiat et n'est jamais annulé au sein de `SelectGreedy`.

> Le glouton ne revient jamais sur un choix. Les seules restitutions de pièces (`palStock.Give`) arrivent plus tard, quand un palonnier est supprimé parce qu'il lui manque une ligne de mother, ou parce qu'une ligne dont il dépendait n'a pas pu être construite. Jamais pour essayer un meilleur découpage.

#### Pourquoi l'ordre du plan est épinglé

Puisque l'algorithme est glouton et que ses départages retombent sur l'ordre de génération, deux lectures du même plan dans deux ordres différents donnent deux implantations différentes. `PlanSource.Read` fige donc l'ordre avant que l'optimiseur ne soit appelé :

```
var ordered = plan
    .OrderBy(c => c.Numero ?? int.MaxValue)
    .ThenBy(c => c.Name, AliantWebS.Reporting.NaturalStringComparer.Instance)
    .ToList();
```

Le Numéro d'abord quand il est renseigné, puis le nom **au sens naturel** — T2 avant T10, pas l'inverse. Le commentaire du code le dit sans détour : l'optimiseur remplit chaque treillis avec les charges dans l'ordre où il les rencontre.

Cet ordre se propage partout : il décide quelle charge crée un paquet de x et devient sa clé (§ 7.1), il décide l'ordre de `Combinations`, donc l'ordre des groupes à effectif et portée égaux, donc le gagnant de chaque tour. Du côté des pièces, `PieceStock.Best` parcourt les modèles triés par nom (`OrderBy(m => m, StringComparer.OrdinalIgnoreCase)`) « pour que le choix ne dépende jamais de l'ordre des fichiers ».

Il reste une dépendance non épinglée, qu'il faut connaître : `xBuckets` est un `Dictionary<double, List<int>>` et le code parcourt ses clés (`foreach (double kx in xBuckets.Keys)`). L'énumération d'un dictionnaire .NET sans suppression suit l'ordre d'insertion, donc le résultat est en pratique déterministe ici — mais ce n'est pas une garantie du langage, et c'est le seul point de l'algorithme qui ne repose pas sur un tri explicite.

Source : `Shared/ScenarioHelpers/Core/Optimizer.cs`, `SelectGreedy` et `PreferGroup` ; `Shared/ScenarioHelpers/PlanSource.cs`, `Read` ; `Shared/ScenarioHelpers/Core/Assembly.cs`, `PieceStock.Best`.

### 7.5 Dimensionner le palonnier retenu

Le groupe retenu par le glouton porte déjà son assemblage, choisi et retiré du stock par `SelectGreedy`. `BuildPalonnier` en fait un objet `Palonnier`.

```
var allowed = table.At(assembly.Type, group.Span);
var pal = new Palonnier
{
    Bar       = assembly.AsBar(allowed.Kg ?? 0),
    Assembly  = assembly,
    Allowed   = allowed,
    Direction = group.Type,
    A = group.A, B = group.B,
    Charges = group.Charges,
};
pal.ComputeReactions();
pal.SelfWeightKg = Math.Max(0, assembly.Pieces.Sum(p => massOf?.Invoke(p) ?? p.WeightKg ?? 0));
```

#### La règle de vérification, en une phrase

**La somme de toutes les charges du palonnier doit tenir dans la *charge ponctuelle au milieu* de son modèle, lue à la portée entre ses deux extrémités.**

C'est la règle du propriétaire appliquée au cas le plus défavorable : entre deux points d'accroche voisins, les charges peuvent s'additionner jusqu'à la valeur du modèle à la longueur immédiatement supérieure à leur écartement, **où qu'elles se trouvent entre eux**. Un palonnier suspendu par ses seules deux extrémités n'a que deux points d'accroche voisins : A et B. La portée A→B est donc l'écartement à considérer, et toutes ses charges y comptent.

La vérification finale, à l'étape 7 de `Optimize` :

```
if (p.Allowed is null || !p.Allowed.Carries(p.TotalWeight))
    capErrors.Add($"{p}: {p.TotalWeight:F0}kg pour {p.Allowed?.Kg ?? 0:F0}kg admissibles sur {p.Span:F2}m");
```

Trois choses à savoir sur ce contrôle :

- `TotalWeight` est `Charges.Sum(c => c.Weight)` : **les charges seules**. Le poids propre du palonnier n'est pas vérifié contre sa propre valeur admissible.
- La valeur est lue à la **portée**, pas à la longueur assemblée. Un palonnier de 4,20 m posé sur 3,70 m est jugé à 3,70 m.
- Le contrôle est fait avec deux appuis, alors que le palonnier gagnera peut-être des appuis intermédiaires à l'étape 5 (`HangOnCrossings`). Des appuis en plus ne peuvent que soulager : le dimensionnement est donc conservateur, et ce n'est pas un oubli.

#### L'exemple complet : le palonnier de L16

Reprenons le tour 5 du § 7.4. L16 pèse 246 kg, au point de grille (−8,98 ; 1,02). Son groupe est {L16} seul, lignes encadrantes 5 (y = 0) et 4 (y = 3,700), portée **3,700 m**. Assemblage retenu : **S500 Tri Alu, 2,8 m + 1,4 m**, longueur 4,20 m, masse 31 + 16 = **47 kg**.

La valeur admissible n'existe pas dans l'abaque à 3,700 m — les lignes S500 Tri Alu font 1,4 m et 2,8 m — elle est donc calculée à moment égal depuis la ligne de 2,8 m (2 031 kg, 31 kg) :

```
p  = 31 / 2,8                          = 11,0714 kg/m
M  = 2031·2,8/4 + 11,0714·2,8²/8       = 1421,70 + 10,85 = 1432,55 kg·m
P  = 4·(1432,55 - 11,0714·3,70²/8) / 3,70
   = 4·(1432,55 - 18,95) / 3,70
   = 5654,4 / 3,70                     = 1528,2 kg
```

Portée 3,700 m ≤ 2 × 2,8 + 0,01 = 5,61 m : l'extrapolation est autorisée. **246 kg pour 1 528 kg admissibles** : le palonnier passe très largement. L'abaque marque cette valeur comme calculée et non lue, et l'étude le signale au lecteur.

Les réactions, par bras de levier sur l'axe A→B (`Geometry.Reactions2Pt`, arrondi au gramme) :

```
s (projection de L16 sur l'axe) = 1,02 m
RB = 246 × 1,02 / 3,700 =  67,816 kg      (ligne 4)
RA = 246 - 67,816       = 178,184 kg      (ligne 5)
```

`Reactions2Pt` et `SupportReactions` donnent le même résultat quand il n'y a que deux appuis aux deux bouts : la première calcule un moment global, la seconde encadre chaque charge par les deux appuis qui la bordent, et les deux formules coïncident ici.

#### Où part le poids propre

Les 47 kg du treillis suivent un chemin distinct de celui des charges, et c'est le point le plus facile à se représenter de travers.

`SetReactions` appelle `Helpers.SelfWeightDistribution.Reactions(positions des appuis, p.Span, p.SelfWeightKg)`. La masse est répartie **uniformément sur la portée dessinée** (3,700 m), pas sur la longueur du treillis pris en stock (4,20 m) : la surlongueur existe physiquement, mais l'endroit où elle déborde n'est enregistré nulle part, et étaler la masse sur le segment dessiné garde le total exact, ce que les études réconcilient. Avec deux appuis aux deux extrémités, le résultat est la moitié de chaque côté :

```
W(ligne 5) = 23,5 kg        W(ligne 4) = 23,5 kg
```

Chaque appui porte donc `R` (les charges) et `W` (sa part du poids propre), stockés séparément dans `PalonnierSupport`. Ensuite :

- Sur la ligne de mother, l'appui devient un `SuspensionPoint { F = R, W = W }`.
- Le contrôle de travée les additionne : `Mother.BayLoadsOf` fusionne les points par `p.F + p.W`. La ligne 5 voit donc **178,184 + 23,5 = 201,7 kg** à l'abscisse x = −8,98.
- Le report aux poutres, lui, n'utilise que `F` : `Mother.Compute` fait `ColumnLoads[col] += p.F * share`. **Le poids propre des palonniers n'apparaît pas dans les charges aux points de grille.** C'est cohérent avec le contrôle d'équilibre de l'étape 7, qui compare le total des points de grille au total des charges portées avec une tolérance de 2,0 kg — mais un lecteur qui additionne les points de grille n'y retrouvera pas les 47 kg.

Une masse inconnue compte pour zéro : `massOf?.Invoke(p) ?? p.WeightKg ?? 0`. Une pièce sans poids propre dans l'inventaire allège donc silencieusement l'implantation. `Assembly.WeightKg`, lui, vaut `null` dans ce cas, et l'étude le signale.

#### Les refus, et ce qu'ils rendent au stock

Deux refus peuvent encore effacer le palonnier après sa construction :

- `« {palonnier} : aucune ligne de mother à l'une de ses extrémités. »` — quand `MotherLines.LineAt` ne trouve pas de ligne sous A ou sous B, ou quand les deux extrémités tombent sur la même ligne. Les deux cas sont des garde-fous : A et B sont par construction sur deux lignes de points différentes, et les demandes qui ont créé les lignes viennent justement de A et B.
- La suppression en cascade, quand une ligne de mother dont il dépend n'a pas pu être assemblée.

Dans les deux cas, `palStock.Give(p.Assembly!)` rend les pièces, et le palonnier est retiré de la liste. Les charges qu'il portait redeviennent non portées et sont signalées à l'étape 7. L'inventaire reste juste : rien dans l'implantation n'utilise plus de pièces que `Palonnier.xlsx` et `MotherGrill.xlsx` n'en contiennent, et `PieceStock.Take` lève `« Stock insuffisant pour … »` plutôt que de prêter une pièce absente.

> Ce que le code ne fait pas : il ne vérifie jamais le palonnier sous son propre poids, ni la flèche, ni un quelconque moment de torsion dû à l'excentrement latéral toléré (8 cm pour un groupe Y, 20 cm pour un biais). Il ne vérifie pas non plus que l'assemblage est physiquement réalisable — deux pièces de modèles identiques mais de marques différentes ne sont pas distinguées, et l'endroit des jonctions est ignoré par construction.

Source : `Shared/ScenarioHelpers/Core/Optimizer.cs`, `BuildPalonnier`, `HangAtEnds`, `SetReactions`, `SupportReactions` et `Build` (étape 7) ; `Shared/ScenarioHelpers/LoadTable.cs`, `At` et `AllowedLoad.Carries` ; `Shared/SelfWeightDistribution.cs`, `Reactions` ; `Shared/ScenarioHelpers/Models/Models.cs`, `Palonnier` et `Mother.BayLoadsOf`.

## Chapitre 8 — Les lignes de mother

Un mother n'est pas une barre posée sous une charge. C'est une **ligne** : une poutre continue tendue au-dessus d'une ligne de points, en travers des poutres de la charpente, posée sur chaque poutre qu'elle croise. Tout ce que la production suspend finit sur une de ces lignes — soit directement, soit par l'intermédiaire d'un palonnier. Ce chapitre explique comment l'optimiseur décide **où** ces lignes vont, **de quoi** elles sont faites, et **quand** il refuse d'en construire une.

Deux repères de vocabulaire, valables pour tout le chapitre. Une **travée** est l'intervalle entre deux poutres voisines. Un **point d'accroche** est un point de la charpente (E5, G9, D3…) sur lequel une ligne repose.

### 8.1 De la demande à la ligne : quelles travées sont nécessaires

#### Le problème

L'optimiseur reçoit une liste d'endroits qu'il faut atteindre sur une ligne de points donnée : l'extrémité d'un palonnier, ou une charge accrochée directement sur la ligne. La réponse naïve serait « une barre par endroit à atteindre ». Elle échoue pour deux raisons. D'abord, une barre doit **reposer sur une poutre à chacun de ses bouts** : une barre de 1 m sous un moteur ne repose sur rien. Ensuite, deux endroits situés dans deux travées voisines donneraient deux barres distinctes qui devraient être accrochées toutes les deux à la poutre commune — ce que les études manuscrites ne font jamais. Elles tracent **une ligne continue**.

#### La règle du code

L'optimiseur construit d'abord la liste des demandes (`Optimizer.Build`, étape 3 — les étapes 3 à 7 vivent dans `Build`, la construction que le glouton et la recherche exacte partagent) :

- les deux extrémités A et B de chaque palonnier retenu, chacune ramenée à la ligne de points la plus proche ;
- la position X de chaque charge posée directement sur une ligne de points.

Une charge est « directement sur une ligne » quand son Y est à `Geometry.TOL` = **0,08 m** ou moins de celui de la ligne. Sinon elle part sur un palonnier. Attention : la ligne de mother est tracée au Y **de la ligne de points**, pas au Y de la charge. Une charge à 7 cm de la ligne 3 est donc accrochée comme si elle y était, sans avertissement.

Ensuite, `MotherLines.Plan` traite chaque ligne de points séparément, dans l'ordre croissant du numéro de ligne. Pour la ligne courante il tient un booléen par travée, `needed[k]`. Chaque demande allume la travée qui la contient strictement. Puis les travées allumées sont regroupées en **suites de travées consécutives**, et chaque suite donne **une** ligne de mother, de la poutre de gauche de la première travée à la poutre de droite de la dernière.

Une travée vide coupe la ligne. Deux demandes séparées par une travée sans rien donnent donc deux lignes distinctes sur la même ligne de points, et non une seule longue.

#### La grille réelle d'Antarès

`Geometry` lit les coordonnées relevées sur site (`CharpenteCoordinates`, en millimètres) et les ramène en mètres autour de **E5**, qui est à 33,103 m de J5. Les poutres retenues par l'optimiseur sont **I, H, G, F, E, D, C, B, A** — neuf poutres, donc **huit travées** :

| Travée | Longueur |
|---|---|
| I–H | 7,200 m |
| H–G | 7,700 m |
| G–F | 7,250 m |
| F–E | 7,156 m |
| E–D | 7,144 m |
| D–C | 7,250 m |
| C–B | 7,700 m |
| B–A | 7,200 m |

Les huit travées totalisent 58,600 m, soit **7,325 m** en moyenne ; les neuf lignes de points s'étalent sur 30,301 m, soit **3,788 m** en moyenne. Ces deux moyennes ne sont plus calculées nulle part : `Geometry.DX` et `Geometry.DY` existaient, personne ne les lisait, elles ont été retirées du fichier (§ 2.3). Aucune règle ne s'appuie sur une moyenne — chaque travée est planifiée et dimensionnée sur sa propre longueur.

> La poutre **J existe dans la charpente** (J1 à J9, à 3,797 m à gauche de I) mais **ne figure pas dans `Geometry.Cols`**, parce qu'aucune des six études manuscrites ne s'en sert (§ 12.1). Aucune ligne de mother ne reposera jamais sur J. Le commentaire de `PlanDrawer` l'assume : « Beam J is not in the optimiser's grid but is in the charpente, and in the studies' figure ». Conséquence à retenir pour 8.4 : tout ce qui dépasse à gauche de I est du porte-à-faux pour l'optimiseur, alors que le calcul charpente, lui, sait trouver J.

#### Un exemple complet : le plan de 24 charges

Le plan de 24 charges du propriétaire (6 210 kg) est celui de la page Charges, alimenté par le dossier partagé `_common/data` : le `Charges.xlsx` livré dans `wwwroot/exemples` n'en porte que 8, pour 2 516 kg. L'implantation ne lit d'ailleurs jamais `Charges.xlsx` — elle lit les lignes de la page Charges, déjà en axes de la grille ; `StudyAxes.ToGrid` n'intervient qu'à l'import Excel. Dix-huit d'entre elles (3 926 kg) tombent à 5 cm ou moins d'une ligne de points et sont donc accrochées directement : L1 à L15 et L18 à L20, sur les lignes **3, 5 et 7**. Les six autres (2 284 kg) — L16, L17, S1 à S4 — partent sur des palonniers. Total : 6 210 kg.

Avec le stock de `Palonnier.xlsx`, aucun groupe de deux charges n'est réalisable (les portées demandées dépassent toutes 6 m, soit deux fois la plus longue ligne d'inventaire), donc six palonniers d'une charge chacun. Leurs extrémités, ajoutées aux charges directes, donnent ceci :

| Ligne de points | Travées demandées | Ligne de mother | Longueur tracée |
|---|---|---|---|
| 2 | E–D | E2–D2 | 7,144 m |
| 3 | G–F, F–E, E–D | G3–D3 | 21,550 m |
| 4 | G–F | G4–F4 | 7,250 m |
| 5 | G–F, F–E, E–D | G5–D5 | 21,550 m |
| 6 | G–F | G6–F6 | 7,250 m |
| 7 | G–F, F–E, E–D | G7–D7 | 21,550 m |
| 8 | E–D | E8–D8 | 7,144 m |

Sept lignes, 93,438 m de mother tracés. Vérification à la main : 2 × 7,144 + 3 × 21,550 + 2 × 7,250 = 14,288 + 64,650 + 14,500 = 93,438 m.

#### Ce que le code ne fait pas

Il ne fusionne pas deux suites séparées par une travée vide. Il ne place aucune ligne sur une ligne de points sans demande. Il ne pose rien sur la poutre J. Et il ne cherche pas à équilibrer les lignes entre elles : chaque ligne de points est traitée isolément, sans regarder ce que portent ses voisines.

Source : `Shared/ScenarioHelpers/Core/MotherLines.cs`, `Plan` ; `Shared/ScenarioHelpers/Core/Optimizer.cs`, `Build` (étape 3) ; `Shared/ScenarioHelpers/Core/Geometry.cs`.

### 8.2 Une demande au ras d'une poutre

#### Le problème

Une demande tombe exactement sur une poutre, ou à quelques centimètres. Elle n'est strictement à l'intérieur d'aucune travée. La boucle qui cherche `cols[k].X < x < cols[k+1].X` ne trouve rien, et la ligne ne serait jamais créée : la charge resterait non portée. Il faut décider **de quel côté** la ligne va s'étendre.

#### La règle du code

Avant de chercher une travée, `Plan` teste la proximité d'une poutre :

```
pour c de 0 à n-1 :
    si |x - cols[c].X| <= TOL  (0,08 m)  ->  la demande est SUR la poutre c
```

Ces demandes sont mises de côté dans `onBeams` et traitées après toutes les autres, une seule fois par poutre (`Distinct()`, ordre croissant). Pour chaque poutre `c` :

1. Si une travée voisine est déjà nécessaire — `needed[c-1]` à gauche ou `needed[c]` à droite — on ne fait rien : la ligne atteint déjà cette poutre.
2. Sinon on allume **une** travée, choisie ainsi :

| Poutre | Travée allumée |
|---|---|
| I (première) | I–H, à sa droite |
| A (dernière) | B–A, à sa gauche |
| Poutre de X > 0 (D, C, B) | la travée à sa gauche, vers le milieu |
| Poutre de X ≤ 0 (H, G, F, **E**) | la travée à sa droite, vers le milieu |

Le pivot est écrit `cols[c].X > 0`. E est à X = 0 exactement, donc **E part vers la droite**, c'est-à-dire vers la travée E–D. C'est une convention arbitraire, pas une symétrie : il fallait trancher.

#### Exemple chiffré

Un seul moteur de 900 kg sur la ligne 6, à 4 cm à gauche de la poutre C, soit X = 14,354 m (C est à 14,394 m). Aucune autre demande sur la ligne 6.

1. |14,354 − 14,394| = 0,040 m ≤ 0,08 → la demande est sur la poutre C, index 6.
2. Aucune travée voisine allumée. C est à X = 14,394 > 0 → travée `c-1` = 5, c'est-à-dire **D–C**.
3. La ligne est **D6–C6**, longue de 7,250 m, pour tenir un seul point.

La charge, elle, est bien à l'intérieur de la travée D–C (4 cm > 1 cm, voir 8.3), donc elle **est** vérifiée. Et elle se répartit par bras de levier :

```
Supprimer cette ligne : les deux lignes suivantes donnent déjà la répartition juste (part de D = 0,005517 → 4,97 kg ; part de C = 0,994483 → 895,03 kg).
part de D = (14,394 - 14,354) / 7,250 = 0,005517  ->  900 x 0,005517 =   4,97 kg
part de C = (14,354 -  7,144) / 7,250 = 0,994483  ->  900 x 0,994483 = 895,03 kg
```

Presque tout va dans C, ce qui est physiquement juste — mais il a fallu construire 7,25 m de mother pour cela.

#### La même demande 4 cm de l'autre côté

À X = 14,434 m (4 cm **à droite** de C), le test à 8 cm la classe encore sur C, donc la ligne reste **D6–C6**, dont l'extrémité droite `XR` vaut 14,394. Le point est alors 4 cm au-delà du bout tracé. C'est exactement le cas que `Optimizer.OnLine` ramène sur l'extrémité (voir 9.3) : le point est replacé à 14,394, sa charge part entièrement dans la poutre C, et il n'est compté dans aucune travée. C'est le bon résultat : une charge assise sur un appui ne fait fléchir personne.

> Deux tolérances différentes se croisent ici et il ne faut pas les confondre. **0,08 m** (`Geometry.TOL`) décide « cette demande est sur une poutre, elle n'a pas besoin de sa propre travée ». **0,010 m** (`Mother.OnSupportM`) décide « cette charge est sur l'appui, elle ne charge aucune travée ». Entre 1 cm et 8 cm d'une poutre, la ligne est construite comme si la demande était sur la poutre, mais la charge est bel et bien comptée dans la travée et vérifiée.

Source : `Shared/ScenarioHelpers/Core/MotherLines.cs`, `Plan` (la boucle `onBeams`) ; `Shared/ScenarioHelpers/Models/Models.cs`, `Mother.Shares` et `Mother.BayLoadsOf`.

### 8.3 Dimensionner la ligne travée par travée

#### Le problème

Une ligne G3–D3 mesure 21,55 m. Lui attribuer une « capacité » unique n'a aucun sens : ce qu'une poutre treillis admet **diminue quand la portée augmente**, et ici la ligne repose sur quatre points, pas deux. Ce qui compte n'est pas la longueur totale mais **chaque travée prise seule**.

#### La règle du code

`MotherLines.Size` applique la règle du propriétaire, celle que les études manuscrites utilisent : la **somme des charges situées strictement entre deux poutres voisines**, où qu'elles soient dans la travée, est comparée à la **charge ponctuelle au milieu** que l'inventaire admet pour ce modèle **à la longueur immédiatement supérieure** à la travée. Aucune correction de position : une charge au quart de la travée compte comme une charge au milieu. C'est conservateur, et c'est la convention.

Le déroulé exact :

1. `loads = line.BayLoadsOf(points)` — un total par travée.
2. Un modèle « porte » la ligne si, **pour chaque travée chargée** (`loads[k] > 1e-9`), `table.At(modèle, BayLength(k)).Carries(loads[k])` est vrai. Une travée à charge nulle n'est pas testée.
3. La longueur à atteindre est `needed = max(0,01 ; Length − TOL)`. Une ligne peut donc être **8 cm plus courte** que le segment tracé.
4. `stock.Best(needed, Carries)` choisit l'assemblage : **le moins de pièces**, puis **le moins de longueur en trop**, puis **la plus légère**, puis l'ordre alphabétique du modèle pour que le résultat ne dépende jamais de l'ordre des fichiers.
5. Les pièces sont retirées du stock (`PieceStock.Take`), `BayAllowed` est rempli travée par travée, et `Bar` reçoit comme capacité **la plus faible valeur parmi les travées connues**.

`AllowedLoad.Carries(kg)` accepte `kg <= admissible + 1e-6`. La marge de 1 mg absorbe les arrondis, rien de plus.

#### L'inventaire d'exemple

`MotherGrill.xlsx` livré avec l'application contient six lignes :

| Modèle | Longueur | Nombre | Poids propre | Charge ponctuelle au milieu |
|---|---|---|---|---|
| S500 Carrée Acier | 1,4 m | 5 | 28,8 kg | 8 000 kg |
| S500 Carrée Acier | 2,8 m | 9 | 47,8 kg | 5 733 kg |
| S500 Carrée Acier | 5,6 m | 39 | 95,6 kg | 3 429 kg |
| C500F / TABMA | 1,4 m | 2 | 28,8 kg | 8 000 kg |
| C500F / TABMA | 5,6 m | 10 | 47,8 kg | 5 430 kg |
| S500 Carrée Acier | 7,0 m | **0** | 96,0 kg | 2 000 kg |

La dernière ligne a `Nombre = 0` : elle **n'est jamais une pièce**, elle ne sert qu'à donner une valeur à 7,0 m. Aucune travée d'Antarès ne mesure 7,0 m ou moins, donc **toutes les valeurs des mothers sont extrapolées**.

#### La valeur au-delà de la plus longue ligne

`LoadTable.At` calcule alors à moment égal depuis la plus longue ligne du modèle :

```
p  = Poids propre / L0          (kg/m)
M  = P0 x L0 / 4 + p x L0² / 8
P(L) = 4 x (M - p x L² / 8) / L
```

Limites : jamais au-delà de `ExtrapolationLimit` = **2,0 × L0**, et seulement si le poids propre est connu. Sinon la valeur est absente et la vérification devient un trou déclaré.

Pour S500 Carrée Acier à la travée E–D de 7,144 m :

```
p = 96 / 7        = 13,714286 kg/m
M = 2000 x 7 / 4 + 13,714286 x 49 / 8 = 3500 + 84    = 3584 kg.m
P = 4 x (3584 - 13,714286 x 51,036736 / 8) / 7,144
  = 4 x (3584 - 87,49) / 7,144 = 13 986,03 / 7,144   = 1 957,7 kg
```

Pour C500F / TABMA, L0 = 5,6 m, P0 = 5 430 kg, poids propre 47,8 kg :

```
p = 47,8 / 5,6 = 8,535714 kg/m
M = 5430 x 5,6 / 4 + 8,535714 x 31,36 / 8 = 7602 + 33,46 = 7 635,46 kg.m
P = 4 x (7635,46 - 54,46) / 7,144 = 30 324,01 / 7,144    = 4 244,7 kg
```

| Travée | S500 Carrée Acier | C500F / TABMA |
|---|---|---|
| 7,144 m (E–D) | 1 957,7 kg | 4 244,7 kg |
| 7,156 m (F–E) | 1 954,3 kg | 4 237,5 kg |
| 7,250 m (G–F) | 1 927,7 kg | 4 181,7 kg |
| 7,700 m (H–G) | 1 809,0 kg | 3 933,6 kg |

#### La ligne G3–D3, en entier

Charges retenues par travée (le détail des réactions de palonnier est fait en 9.2) :

| Travée | Ce qui tombe dedans | Total |
|---|---|---|
| G–F (7,250 m) | L13 204 kg + L18 131 kg | 335,0 kg |
| F–E (7,156 m) | L4 182 kg + L7 265 kg + L10 159 kg | 606,0 kg |
| E–D (7,144 m) | L1 154 kg + extrémité du palonnier S1 333,904 kg + extrémité du palonnier S2 221,575 kg | 709,479 kg |

Les deux modèles passent partout. Le choix se joue donc sur l'assemblage. `needed = 21,550 − 0,08 = 21,470 m`, soit une cible de **2 147 cm** (`Assembler` travaille en centimètres entiers, `ceil(needed × 100 − 1e-6)`).

```
3 pièces au maximum : 3 x 5,6 = 16,8 m = 1680 cm  <  2147   -> impossible
4 pièces S500      : 4 x 5,6 = 22,4 m = 2240 cm, trop de 93 cm, masse 382,4 kg
4 pièces C500F     : 4 x 5,6 = 22,4 m = 2240 cm, trop de 93 cm, masse 191,2 kg
```

Même nombre de pièces, même longueur en trop : c'est **la masse** qui départage. La ligne G3–D3 est donc un **C500F / TABMA**, 4 × 5,6 m = 22,4 m d'assemblage pour 21,55 m tracés. Les 0,85 m en trop dépassent quelque part ; où exactement n'est enregistré nulle part.

`BayAllowed` vaut alors [4 181,7 ; 4 237,5 ; 4 244,7] kg et `Bar.Capacity` = 4 181,7 kg, la plus faible.

#### Le stock s'épuise, et la géométrie ne suffit plus

Les lignes sont dimensionnées **dans l'ordre des lignes de points croissantes**. Sur le plan de 24 charges, la ligne 3 puis la ligne 5 prennent chacune 4 des 10 pièces de C500F 5,6 m. Quand vient la ligne 7 — géométriquement identique, G7–D7, 21,550 m — il n'en reste que 2 : `PieceStock.Cheapest` ne peut plus atteindre 21,47 m avec ce modèle et renvoie null. La ligne 7 est donc un **S500 Carrée Acier**, 4 × 5,6 m, 382,4 kg. Deux lignes identiques, deux modèles différents : la seule raison est l'ordre de passage.

> Le poids propre de la ligne elle-même **n'est pas comparé** à la valeur de l'abaque. La convention des études est que la charge ponctuelle au milieu est une charge **utile** : ce qu'on a le droit de pendre à la poutre, pas ce qu'elle pèse. Le poids propre est en revanche bien transmis aux points d'accroche par le calcul (voir 9.4).

Source : `Shared/ScenarioHelpers/Core/MotherLines.cs`, `Size` ; `Shared/ScenarioHelpers/LoadTable.cs`, `At` ; `Shared/ScenarioHelpers/Core/Assembly.cs`, `PieceStock.Best` et `Assembler.Cheapest`.

### 8.4 Le porte-à-faux

#### Le problème

Une demande tombe à gauche de la poutre I ou à droite de la poutre A. Il n'y a pas de travée là-bas : la grille s'arrête. On ne peut ni ignorer la demande, ni inventer un appui.

#### La règle du code

Dans `Plan`, avant tout autre test :

```
si x < cols[0].X - TOL      -> needed[0]   = vrai ; overLeft  = max(overLeft,  cols[0].X - x)
si x > cols[n-1].X + TOL    -> needed[n-2] = vrai ; overRight = max(overRight, x - cols[n-1].X)
```

Puis, quand la suite de travées est transformée en ligne :

```
XL = cols[k0].X   - (k0   == 0   ? overLeft  : 0)
XR = cols[k1+1].X + (k1+1 == n-1 ? overRight : 0)
```

Autrement dit : la première travée (ou la dernière) est allumée, et le segment tracé est **prolongé jusqu'à la demande la plus éloignée**, exactement. Comme `needed[0]` est allumé, la première suite commence forcément à la travée 0, donc `overLeft` s'applique toujours à la bonne ligne ; même raisonnement à droite.

#### Comment la charge se répartit

`Mother.Shares` cherche la poutre à gauche et la poutre à droite du point. Au-delà de l'extrémité il n'y en a qu'une seule, alors le code retombe sur **les deux poutres les plus proches** et applique la même formule affine. Les parts somment toujours à 1, mais l'une dépasse 100 % et l'autre devient négative — c'est le soulèvement.

Exemple : 1 000 kg à 2,00 m à gauche de la poutre I, sur une ligne I–H (7,200 m).

```
part de I = (H.X - x) / (H.X - I.X) = (-22,106 + 31,306) / 7,200 = 9,200 / 7,200 = +1,27778
part de H = (x - I.X) / (H.X - I.X) = (-31,306 + 29,306) / 7,200 = -2,000 / 7,200 = -0,27778
I  : +1 277,8 kg          H : -277,8 kg          somme : 1 000,0 kg
```

C'est rigoureusement la formule que le calcul charpente applique de son côté (`loadClosest = W × (span + cantilever) / span`, `loadFarthest = −W × cantilever / span`) : 1 000 × (7,2 + 2) / 7,2 = 1 277,8 et −1 000 × 2 / 7,2 = −277,8.

#### Ce que le porte-à-faux n'est PAS

`Mother.BayLoadsOf` ne compte que ce qui est **strictement à l'intérieur** d'une travée. Une charge en porte-à-faux n'est donc dans aucune travée. `Mother.CantileverLoad` sait la mesurer, mais **aucun code de dimensionnement ne l'appelle** : ni `Size`, ni `WorstBay`, ni `Overload`. L'optimiseur ne dimensionne donc **rien** pour le porte-à-faux d'une ligne de mother, et ne le refuse jamais.

Ce trou est assumé, pas caché : le rapport le déclare. `ReportDataBuilder.BuildMotherAbaqueChecks` produit un contrôle `AbaqueLoadCase.Cantilever` pour chaque extrémité chargée en porte-à-faux, avec la charge en déport et la portée atteinte au-delà du dernier appui — le moment n'est calculé que pour classer ces contrôles, du plus défavorable au moins — et le texte du Word dit que l'inventaire ne donne aucune charge admissible en déport et que la vérification n'a pas pu être faite. Le soulèvement (`AbaqueLoadCase.Uplift`, au-delà de `UpliftToleranceKg` = 0,5 kg) est déclaré de la même façon.

> Un piège réel, dû à l'absence de la poutre J, et c'est la dissymétrie annoncée au § 2.3. Une ligne prolongée à gauche de I d'un porte-à-faux `o` est tracée depuis `XL = I.X − o`. Le calcul charpente cherche les points d'accroche par `CharpenteSupportsOf`, qui balaie **tout** `CharpenteCoordinates` avec une tolérance de **0,5 m** au-delà des bouts — J comprise. J est à 3,797 m de I. Dès que `o ≥ 3,297 m`, le calcul trouve **J** sous la ligne et la traite comme un appui — alors que l'optimiseur l'a dimensionnée comme un porte-à-faux sans appui, puisqu'il ne connaît pas cette poutre. Les deux ne parlent alors plus de la même poutre. C'est le point ouvert du § 12.1 : l'absence de J est justifiée du côté de l'implantation, elle ne l'est pas du côté du calcul de charge.

Source : `Shared/ScenarioHelpers/Core/MotherLines.cs`, `Plan` ; `Shared/ScenarioHelpers/Models/Models.cs`, `Mother.Shares` et `Mother.CantileverLoad` ; `Shared/ScenarioHelpers/Report/ReportDataBuilder.cs`, `BuildMotherAbaqueChecks`.

### 8.5 Quand une ligne ne passe pas

#### Les quatre refus

`Size` renvoie `false` et un message en français, construit par `Explain`. L'en-tête est toujours `Ligne de mother {ColL}{Row}–{ColR}{Row}`, par exemple « Ligne de mother G3–D3 ». Les quatre branches, dans l'ordre où elles sont testées :

1. Aucun modèle en stock :

```
Ligne de mother G3–D3 : aucune pièce de MotherGrill.xlsx n'est en stock.
```

2. Au moins un modèle porte la ligne d'après l'abaque, mais les pièces restantes ne suffisent pas à atteindre la longueur :

```
Ligne de mother G3–D3 : les pièces restant en stock ne permettent pas
d'assembler 21,47 m d'un modèle qui la porte.
```

La longueur citée est `needed`, c'est-à-dire déjà diminuée des 8 cm.

3. La ligne n'a aucune travée (cas défensif : `Plan` ne produit jamais cela) :

```
Ligne de mother G3–D3 : aucun modèle de MotherGrill.xlsx ne convient.
```

4. Le cas courant — aucun modèle ne porte une travée :

```
Ligne de mother G3–D3 : 4 800 kg sur la travée E3–D3 (7,14 m),
alors que le modèle le plus fort en stock y admet 4 245 kg.
```

#### Ce que le message désigne exactement

La travée citée est **la plus chargée** (`OrderByDescending(k => loads[k])`), pas celle dont le taux d'utilisation est le pire. C'est une différence réelle avec `Mother.WorstBay`, utilisé partout ailleurs, qui classe par le rapport charge / admissible — parce qu'une charge plus faible sur une travée plus longue est souvent celle qui dépasse la première. Le message peut donc pointer une travée qui n'est pas la coupable la plus flagrante.

La valeur citée est `max` sur **les modèles en stock** de la valeur lue à la portée de cette travée. « En stock » qualifie le modèle, pas la ligne d'inventaire : la valeur peut très bien venir d'une ligne à `Nombre = 0` du même modèle, comme la ligne 7,0 m de S500 Carrée Acier.

Détail d'écriture : `worst` est calculé avant le garde `loads.Length == 0`. C'est sans conséquence — `FirstOrDefault()` renvoie 0 sur une séquence vide et le garde sort avant d'utiliser l'index — mais l'ordre surprend à la lecture.

#### La cascade de repli

Dans `Optimizer.Build`, étape 4, une ligne qui échoue est mise dans `failed` et son message rejoint `errors`. Ensuite :

1. Tout palonnier dont `MotherA` ou `MotherB` est une ligne en échec est **retiré**, et ses pièces rendues (`palStock.Give`).
2. Les lignes en échec sont retirées de la liste.
3. Une ligne survivante qui n'a plus rien à porter — ni charge directe, ni palonnier accroché à l'une de ses extrémités — est rendue au stock (`motherStock.Give`) et retirée.

`PieceStock.Give` re-crédite au maximum jusqu'à `StockInit` : rendre deux fois la même pièce ne crée pas de stock.

À l'étape 7, toute charge qui n'est portée ni par un palonnier ni par un mother produit :

```
S1 (550 kg) n'est portée ni par un palonnier ni par un mother.
```

et entre dans `result.Uncovered`. `Pipeline.GenerateLayout` **refuse alors d'enregistrer l'implantation** : il lève une exception dont le texte est produit par `Pipeline.UncoveredMessage`, qui liste les charges (au plus 8, puis « (+n) »), ajoute le diagnostic de stock quand le plus long assemblage possible est plus court que le plus petit écart entre poutres ou entre lignes de points, recopie jusqu'à trois messages de lignes en échec, signale le cas échéant le réglage « orientations orthogonales uniquement », et finit par « L'implantation existante n'a pas été modifiée. »

> Une seule ligne impossible peut faire tomber des palonniers parfaitement dimensionnés : c'est l'étape 1 de la cascade. Le message d'erreur qui explique le refus final porte sur la charge non portée, pas sur la ligne. Il faut remonter aux messages « Ligne de mother … » dans `result.Errors` pour connaître la vraie cause.

Source : `Shared/ScenarioHelpers/Core/MotherLines.cs`, `Size` et `Explain` ; `Shared/ScenarioHelpers/Core/Optimizer.cs`, `Build` ; `Shared/ScenarioHelpers/Pipeline.Messages.cs`, `UncoveredMessage`.

## Chapitre 9 — Accrocher, répartir, vérifier

Les lignes de mother sont posées et dimensionnées. Restent trois questions : à **combien** de lignes un palonnier pend, **comment** ses charges se partagent entre elles, et **qui vérifie** que le résultat tient. Ce chapitre les traite dans cet ordre, puis suit les charges jusqu'aux points d'accroche.

### 9.1 Un palonnier s'accroche à chaque mother qu'il croise

#### Le problème

Un palonnier court le long des poutres, donc **en travers des lignes de points**. Les lignes de points sont espacées de 3,6 à 4,1 m. Un palonnier de 11 m en croise donc deux ou trois entre ses extrémités. La réponse naïve — « il pend de ses deux bouts » — est fausse à la salle : les riggers l'accrochent **partout où il passe sous un mother**. Et elle est dangereuse dans les deux sens : elle charge trop les deux lignes d'extrémité, et elle ne charge pas du tout les lignes du milieu, qui seront pourtant tendues en vrai.

La règle du propriétaire (relayée le 2026-09-11) est donc : **un palonnier pend de chaque mother qu'il croise, et chaque charge va aux deux accroches qui l'encadrent, une de chaque côté.** Avec une exception : une mother que l'accrochage ferait dépasser sa valeur n'est pas accrochée.

#### La règle du code

`Optimizer.HangOnCrossings` est exécuté une fois que tous les palonniers et toutes les lignes sont placés.

1. Remise à zéro : chaque palonnier est ramené à ses deux extrémités (`HangAtEnds`), et ses réactions recalculées.
2. Recensement des croisements intermédiaires. Pour un palonnier dont les deux bouts ne sont pas au même Y, on ne regarde que les lignes **strictement entre** les deux bouts : `m.Y > min(A.Y, B.Y) + TOL` et `m.Y < max(A.Y, B.Y) − TOL`. La bande de 8 cm écarte les lignes des extrémités, déjà accrochées.
3. L'abscisse du croisement est interpolée : `X = A.X + (m.Y − A.Y) / (B.Y − A.Y) × (B.X − A.X)`. Pour un palonnier le long des poutres, A.X = B.X, donc X est simplement l'abscisse du palonnier.
4. Le croisement compte si `XL − CrossingTol ≤ X ≤ XR + CrossingTol`, avec `CrossingTol = TOL + 1e-6` = **0,080001 m**. Le micromètre absorbe les arrondis entre les deux calculs qui utilisent cette constante.
5. Les lignes trouvées sont **groupées par ligne de points** : un croisement, ce n'est pas une mother, c'est un endroit. Les lignes trouvées sont groupées par ligne de points : un croisement, ce n'est pas une mother, c'est un endroit. En pratique une seule mother y est candidate — `Plan` ne pose qu'une ligne par suite de travées consécutives, et deux lignes d'une même ligne de points sont séparées par au moins une travée entière. Le groupement et le classement par `XL` puis par `Id` sont défensifs. Les candidates sont classées par `XL` puis par `Id`.
6. La liste est triée par identifiant de palonnier puis par distance le long du palonnier, pour que le résultat ne dépende pas de l'ordre d'énumération.
7. Boucle : pour chaque croisement pas encore accroché, on essaie chaque candidate. On construit l'état d'essai (`WithSupport`), on demande à `Overload` si une travée dépasserait ; si non, l'accrochage est adopté. **On recommence tant qu'un tour a fait progresser quelque chose** : accrocher un palonnier soulage les lignes de part et d'autre, ce qui peut libérer la place pour un croisement refusé plus tôt.
8. Ce qui n'a pas pu être accroché est réévalué et enregistré dans `Palonnier.Skipped`, **une entrée par candidate**, avec la mother surchargée, la charge qu'elle aurait portée et sa valeur admise.
9. Enfin, les points de chaque ligne sont **reconstruits** depuis les accroches (`m.Points.Clear()` puis `PointsOn`), et `RA` / `RB` sont relus sur les accroches d'extrémité.

#### Le test de refus

`Overload` examine, dans l'ordre, la mother qu'on ajoute puis toutes les autres concernées. Pour chacune il calcule les charges par travée **avant** et **après**, et ne contrôle que les travées dont la charge **augmente** (`after[k] > before[k] + 1e-9`). Une travée déjà au-dessus de sa valeur, mais pas aggravée, n'est pas un motif de refus. Une travée sans valeur connue (`BayAllowed` absente ou `Kg` nul) fait refuser.

#### Ce qui est écrit en base

`Pipeline.BuildElementEntities` traduit la décision en `HookRecord` stockés en JSON dans `MotherGridPalonnier.Accrochages` : un enregistrement `Hooked = true` par accroche, un `Hooked = false` par croisement laissé, avec sa raison :

```
au placement, une travée du mother « C500F / TABMA L=22,4m #2 » aurait porté
4 800 kg pour 4 245 kg admissibles
```

Le calcul charpente, le rapport et la page qui édite les accrochages lisent tous `PalonnierHanging.Resolve`, unique lecture de la règle. Ses tolérances sont dérivées de celles de l'optimiseur pour qu'elles ne puissent pas dériver : `CrossingToleranceM = Geometry.CrossingTol` (0,080001 m entre les bouts), `ToleranceM = Geometry.TOL + 0,02` = **0,10 m** aux bouts, `SameCrossingM = 0,010 m` pour fusionner deux croisements au même endroit. Priorité : main > optimiseur > défaut (accroché, sur la mother la plus à gauche).

> Un croisement laissé n'est pas une erreur ; c'est une décision. Mais elle est prise **avec les charges du moment du placement**, et le texte de la raison le dit (« au placement, … »). Si quelqu'un modifie les charges ensuite, l'enregistrement reste tel quel jusqu'au prochain placement ou jusqu'à une reprise à la main.

Source : `Shared/ScenarioHelpers/Core/Optimizer.cs`, `HangOnCrossings` et `Overload` ; `Shared/ScenarioHelpers/Pipeline.Entities.cs`, `BuildElementEntities` ; `Shared/PalonnierHanging.cs`, `Resolve`.

### 9.2 La règle du levier, et le porte-à-faux qui soulève

#### La règle du code

`Optimizer.SupportReactions` traite **chaque charge séparément**. Pour une charge à la distance `s` le long du palonnier :

1. `a` = l'accroche de plus grand `S` telle que `S ≤ s` ; `b` = l'accroche de plus petit `S` telle que `S > s`.
2. Si les deux existent, bras de levier classique.
3. Si l'une manque — la charge est au-delà de la dernière accroche — on prend **les deux accroches les plus proches** et on applique la **même formule affine**. C'est le porte-à-faux.
4. Deux accroches au même endroit (`|span| < 1e-9`) : moitié-moitié.

```
r[a] += W x (S_b - s) / (S_b - S_a)
r[b] += W x (s - S_a) / (S_b - S_a)
```

La somme des deux vaut toujours W, dans les deux cas. Rien d'autre n'est ajouté ni retranché.

#### Exemple chiffré : un palonnier à quatre accroches

Un palonnier le long des poutres, de la ligne 7 (Y = −7,450) à la ligne 4 (Y = +3,700). Portée 11,150 m. Il croise la ligne 6 (Y = −3,700) et la ligne 5 (Y = 0). Une charge de 600 kg à Y = −1,500, soit `s` = 5,950 m depuis A.

Accroches en S = 0 ; 3,750 ; 7,450 ; 11,150.

**Avant** l'étape 5, accroché à ses deux bouts seulement :

```
r[0] = 600 x (11,150 - 5,950) / 11,150 = 600 x 5,200 / 11,150 = 279,82 kg
r[1] = 600 x ( 5,950 -     0) / 11,150 = 600 x 5,950 / 11,150 = 320,18 kg
```

**Après** accrochage aux croisements, la charge est encadrée par 3,750 et 7,450 :

```
r(ligne 6) = 600 x (7,450 - 5,950) / 3,700 = 600 x 1,500 / 3,700 = 243,24 kg
r(ligne 5) = 600 x (5,950 - 3,750) / 3,700 = 600 x 2,200 / 3,700 = 356,76 kg
r(ligne 7) = 0 kg          r(ligne 4) = 0 kg
```

La charge quitte **entièrement** les deux lignes d'extrémité. C'est toute la différence entre les deux règles, et c'est pourquoi l'étape 5 existe.

#### Le poids propre du palonnier

Il ne suit pas la même formule. `SetReactions` appelle `SelfWeightDistribution.Reactions(positions, p.Span, p.SelfWeightKg)`, qui répartit une masse **uniforme** sur la longueur tracée. L'astuce du code : il découpe [0, L] en intervalles gouvernés chacun par **une seule paire d'accroches voisines**, et place la masse de chaque intervalle en son centre de gravité. Comme les coefficients sont affines, l'intégrale est exacte — il n'y a ni erreur de discrétisation ni branche séparée pour le porte-à-faux, qui sort tout seul en coefficient négatif.

Supposons le palonnier ci-dessus assemblé de 4 × 3,0 m, soit **140 kg** (F34300, poids propre 35 kg). La masse linéique est 140 / 11,150 = 12,556054 kg/m.

```
intervalle [0     ; 3,750] : masse 47,085 kg, centre 1,875 -> 23,543 aux S=0 et S=3,750
intervalle [3,750 ; 7,450] : masse 46,457 kg, centre 5,600 -> 23,229 aux S=3,750 et S=7,450
intervalle [7,450 ; 11,150]: masse 46,457 kg, centre 9,300 -> 23,229 aux S=7,450 et S=11,150

ligne 7 : 23,543    ligne 6 : 46,771    ligne 5 : 46,457    ligne 4 : 23,229
somme   : 140,000 kg
```

Ce que reçoit chaque ligne, au total :

| Ligne | Charge scénique (F) | Poids propre (W) | Total |
|---|---|---|---|
| 7 | 0 kg | 23,543 kg | 23,543 kg |
| 6 | 243,24 kg | 46,771 kg | 290,01 kg |
| 5 | 356,76 kg | 46,457 kg | 403,22 kg |
| 4 | 0 kg | 23,229 kg | 23,229 kg |

Les deux lignes d'extrémité ne portent plus la charge, mais elles portent toujours leur part du palonnier. La masse est répartie sur la **longueur tracée** (11,150 m), pas sur les 12,0 m assemblés : c'est la seule géométrie enregistrée, et cela garde la somme exacte.

#### Le porte-à-faux qui soulève

Reprenons le même palonnier accroché seulement aux lignes 7 et 6 (S = 0 et 3,750), la charge de 600 kg restant à s = 5,950 : elle est au-delà de la dernière accroche.

```
porte-à-faux = 5,950 - 3,750 = 2,200 m      travée entre accroches = 3,750 m
ligne 6 (la plus proche) : 600 x (3,750 + 2,200) / 3,750 = +952,0 kg
ligne 7 (la plus loin)   : -600 x  2,200        / 3,750 = -352,0 kg
somme : 600,0 kg
```

La ligne 7 est **tirée vers le haut** de 352 kg. Deux conséquences dans le code :

- `Mother.BayLoadsOf` ignore toute charge `≤ 0`, et `Mother.DownwardLoad` applique `Math.Max(0, ...)`. **Un soulèvement en un point ne libère aucune place ailleurs** : on ne soustrait pas une traction d'une compression pour gagner de la marge.
- Le rapport le signale comme un cas sans valeur d'abaque : « Le mother … est soulevé de N daN par un palonnier en porte-à-faux. Aucune valeur de l'inventaire ne couvre ce cas : il n'a pas pu être vérifié. »

`Mother.Merge` regroupe les points au millimètre près (`Math.Round(p.X, 3)`) et somme `F + W` : deux accroches au même endroit comptent comme une seule charge. Du côté du palonnier, `SelfWeightDistribution.CoincidentToleranceM` = **0,010 m** fusionne deux croisements distants de moins d'un centimètre, sans quoi le bras de levier diviserait par zéro. Le cas est réel : un palonnier dont l'abscisse tombe sur une poutre croise au même point les mothers des deux travées voisines.

Source : `Shared/ScenarioHelpers/Core/Optimizer.cs`, `SupportReactions`, `WithSupport`, `SetReactions` ; `Shared/SelfWeightDistribution.cs`, `Reactions` ; `Shared/ScenarioHelpers/Models/Models.cs`, `Mother.Merge` et `Mother.DownwardLoad`.

### 9.3 La vérification travée par travée

#### Trois endroits, une seule règle

La même question — « cette travée tient-elle ? » — est posée trois fois dans la chaîne, et elle doit recevoir trois fois la même réponse :

| Où | Quoi | Comment il obtient les travées |
|---|---|---|
| `MotherLines.Size` | choix du modèle de la ligne | `Geometry.ColXList`, les poutres de la grille |
| `Optimizer.Overload` | accepter ou refuser un croisement | `Mother.BayAllowed`, figé au dimensionnement |
| `ReportDataBuilder.BuildMotherAbaqueChecks` | contrôle publié dans l'étude | `CharpenteSupportsOf`, les points réellement sous la ligne |

La règle commune : **la somme des charges descendantes strictement à l'intérieur de la travée**, comparée à la charge ponctuelle au milieu du modèle lue à la longueur immédiatement supérieure. « Strictement » veut dire au-delà de `OnSupportM` = **0,010 m** de chaque poutre. Une charge à moins d'un centimètre d'une poutre est **sur** la poutre : elle ne fait fléchir ni la travée de gauche ni celle de droite.

> À exactement 1 cm d'une poutre, les deux tests ne disent pas la même chose : `BayLoadsOf` exige `x > col + 0,010` strictement, donc la charge n'est dans aucune travée, tandis que `Shares` exige `|x − col| < 0,010` strictement, donc elle est répartie par levier entre les deux poutres. Cas de mesure nulle, mais il existe. Et le 10 mm est déclaré **quatre fois** dans la chaîne — `Mother.OnSupportM`, `ReportDataBuilder.OnSupportM`, `OnPointM` dans `PointLoadReactions`, `SelfWeightDistribution.CoincidentToleranceM` — au lieu d'une.

#### L'exemple demandé : la travée E–D de la ligne 3

Trois choses tombent dans cette travée de 7,144 m, entre X = 0,010 et X = 7,134 :

Les deux palonniers S1 et S2 vont de la ligne 3 (Y = 7,400) à la ligne 2 (Y = 11,050), portée **3,650 m**, aux abscisses X = 1,000 et X = 2,000. Leurs charges (550 kg et 350 kg) sont à Y = 9,000, donc à s = 1,600 m de l'extrémité posée sur la ligne 3.

```
palonnier S1 : RA = 550 - 550 x 1,600 / 3,650 = 550 - 241,096 = 308,904 kg
palonnier S2 : RA = 350 - 350 x 1,600 / 3,650 = 350 - 153,425 = 196,575 kg
```

Chacun est assemblé de 2 × 2,0 m de F34200 (poids propre 25 kg), soit 50 kg répartis à parts égales sur deux accroches : **25,000 kg** par bout. Aucune ligne de points ne se trouve entre les lignes 3 et 2, donc aucun croisement intermédiaire : `HangOnCrossings` ne change rien pour ces deux palonniers.

Bilan de la travée :

| Point | X | Provenance | Charge |
|---|---|---|---|
| Extrémité A du palonnier S1 | 1,000 m | 308,904 + 25,000 | 333,904 kg |
| Extrémité A du palonnier S2 | 2,000 m | 196,575 + 25,000 | 221,575 kg |
| Charge L1, directement sur la ligne | 2,990 m | 154 + 0 | 154,000 kg |
| **Total travée E3–D3** | | | **709,479 kg** |

La valeur, pour le C500F / TABMA retenu en 8.3, à la portée de 7,144 m :

```
709,479 kg  <=  4 244,7 kg       ->  la travée passe, à 16,7 % de sa valeur
```

Si la ligne avait été un S500 Carrée Acier, la valeur serait de 1 957,7 kg : elle passerait encore, mais le seuil de refus serait atteint bien plus tôt. C'est le même calcul que fait `Overload` quand un palonnier supplémentaire vient chercher une place dans cette travée : il refuserait à partir du moment où le nouveau total dépasserait 1 957,7 kg.

#### Pourquoi l'accord avec le calcul est le vrai sujet

L'optimiseur ne dimensionne pas « au mieux » : il dimensionne **exactement pour ce que le calcul charpente trouvera**. Si les deux divergent, l'étude publiée contredit la barre choisie, et personne ne sait laquelle croire.

Concrètement, pour la ligne G3–D3 tracée de X1 = −14,406 à X2 = +7,144 au Y = 7,400 : `CharpenteSupportsOf` ajoute Point0 (E5, soit 33,103 ; 0), balaie tous les points de la charpente, garde ceux dont l'écart perpendiculaire est ≤ 0,5 m et dont la projection tombe dans [−0,5 ; longueur + 0,5]. Il trouve G3, F3, E3, D3 aux distances 0 / 7,250 / 14,406 / 21,550 — **les mêmes travées, aux mêmes longueurs** que `Geometry`. H3 serait à −7,700, C3 à +28,800 : tous deux hors bande. L'accord est exact.

Deux choses seulement peuvent le rompre : la poutre J (voir 8.4), et un point déplacé par le rabattement.

#### Le rabattement sur l'extrémité, et l'écart de 0,84 kg à G9

`Optimizer.OnLine(line, x) = Math.Clamp(x, line.XL, line.XR)` est appliqué **à tout ce qui se pose sur une ligne** : la position de chaque accroche de palonnier dans `PointsOn`, la position de chaque charge directe, et la position stockée en base (`ChargeMp.X = OnLine(m, c.X) − m.XL`).

Pourquoi ? Parce qu'un point peut légitimement se trouver quelques centimètres **au-delà** du bout tracé. `Plan` arrête une ligne à une poutre dès qu'une demande est à 8 cm ou moins de celle-ci (8.2) ; `LineAt` accepte une ligne dont le segment atteint le point à `CrossingTol` = 0,080001 m près ; `HangOnCrossings` accepte un croisement dans la même bande. Le déplacement maximal du rabattement est donc d'environ **8 cm**, jamais plus.

Et ce déplacement est **le bon** : le point est ramené sur l'extrémité de la ligne, c'est-à-dire sur la poutre, et sa charge part entièrement dans le point d'accroche (`Shares` : écart nul < 1 cm → 100 %). Le calcul fait le même geste de son côté — `PalonnierHanging.MeetingPoint` rabat le paramètre `u` dans [0, 1] et renvoie un point **sur** la mother, et la position stockée d'une charge directe est déjà rabattue. Sans ce rabattement, l'optimiseur dimensionnerait une travée pour une charge que le calcul poserait sur une poutre, ou l'inverse.

Le cas qui a fait trouver la règle est un désaccord de **0,84 kg au point G9** entre la charge dimensionnée et la charge calculée : un point situé à quelques centimètres au-delà d'un bout de ligne, rabattu d'un côté et pas de l'autre, changeait les bras de levier de la travée voisine juste assez pour produire cet écart. La règle est donc : le rabattement est appliqué **des deux côtés, avec la même constante**, et `Geometry.CrossingTol` est la seule constante qui la porte.

> La géométrie du palonnier, elle, **n'est pas déplacée** : `OnLine` ne modifie ni A, ni B, ni la position des charges le long du palonnier. Seule la position **sur la ligne de mother** est rabattue. Un lecteur qui chercherait le déplacement dans les coordonnées du palonnier ne le trouverait pas.

Source : `Shared/ScenarioHelpers/Models/Models.cs`, `Mother.BayLoadsOf` et `Mother.WorstBay` ; `Shared/ScenarioHelpers/Core/Optimizer.cs`, `Overload` et `OnLine` ; `Shared/ScenarioHelpers/Report/ReportDataBuilder.cs`, `BuildMotherAbaqueChecks` ; `Shared/LoadTransmissionHelper.cs`, `CharpenteSupportsOf`.

### 9.4 Ce qui remonte aux points d'accroche

#### La règle du code

`Mother.Compute` vide `ColumnLoads`, met chaque poutre de la ligne à zéro, puis, pour chaque point posé sur la ligne, ajoute `p.F × part` à chaque poutre concernée selon `Shares` — bras de levier entre les deux poutres encadrantes, formule affine au-delà des extrémités. Chaque total est ensuite arrondi à trois décimales.

`Build`, étape 6, additionne toutes les lignes dans `gridLoads`, clé `(colonne, ligne de points)`, en ignorant les contributions de valeur absolue inférieure à `1e-6` et en arrondissant à trois décimales à chaque accumulation.

#### Exemple : ce que la ligne G3–D3 délivre

Les huit points de la ligne, avec leur `F` seul, donnent :

| Point d'accroche | Charge |
|---|---|
| G3 | 108,71 kg |
| F3 | 450,95 kg |
| E3 | 878,09 kg |
| D3 | 162,73 kg |
| **Somme** | **1 600,479 kg** |

Vérification à la main : la somme des `F` posés sur la ligne vaut 204 + 131 + 182 + 265 + 159 + 308,904 + 196,575 + 154 = 1 600,479 kg. Les parts de `Shares` somment toujours à 1, donc **rien ne se perd et rien ne se crée** à cette étape.

#### Le piège : le poids propre n'est pas là

`Compute` écrit `ColumnLoads[col] += p.F * share`. Il utilise **`F` seul, jamais `W`**. Les 50 kg de poids propre des deux palonniers S1 et S2, qui comptent pourtant dans les 709,479 kg de la travée E–D, **ne figurent pas** dans les 1 600,479 kg ci-dessus.

Ce n'est pas un oubli, c'est un partage de responsabilité :

- Pour la **mother**, le poids propre d'un palonnier est une charge comme une autre : elle pend à elle. Il compte donc dans `BayLoadsOf` (via `Merge`, qui somme `F + W`) et dans la vérification d'abaque.
- Pour les **points d'accroche**, c'est le calcul charpente qui transmet les masses, et lui seul : `AddPalonnierSelfWeights` ajoute la masse d'un palonnier aux mothers **avant** l'étape 2, pour qu'elle suive exactement le chemin de ses charges ; `AddMotherGridSelfWeights` ajoute la masse d'un mother **directement** aux points d'accroche après l'étape 2, parce qu'un mother repose sur le toit et n'a pas de support à chercher.

L'identité que les études réconcilient est donc `somme(points) = somme(charges scéniques) + somme(masses cataloguées)`, et elle est vérifiée par le calcul, pas par l'optimiseur.

#### Les contrôles de fin de course

`Optimize` calcule `carriedTotal`, la somme des poids des charges **effectivement portées**, et `totalGrid`, la somme de `gridLoads`. Puis :

```
balanceOk = |totalGrid - carriedTotal| < 2,0 kg
```

Les 2,0 kg ne sont pas une tolérance physique : c'est la place laissée aux arrondis à trois décimales accumulés ligne par ligne. Si l'écart les dépasse, une charge s'est perdue en route — c'est un détecteur de fuite, pas un contrôle de résistance.

Ensuite `capErrors` reprend tout à zéro :

```
P3(L1) Y x=1,00 [S1]: 550kg pour 490kg admissibles sur 3,65m
M2:C500F / TA L=22,4m G3-D3: travée E-D 4800kg pour 4245kg admissibles
```

le premier pour chaque palonnier dont le total dépasse la valeur à sa portée, le second pour chaque ligne dont `WorstBay` — classée par le rapport charge / admissible — retourne une travée. Ces messages rejoignent `result.Errors` derrière les erreurs de placement.

#### Ce qu'il ne faut pas croire

`result.GridLoads` **n'est pas** le carnet de charges de la salle. Il ne contient que les charges scéniques, pas les poids propres, et il n'est comparé à rien : ni aux capacités des points d'accroche de `TCharpente`, ni surtout aux **totaux par poutre** (ligne 10 de `TCharpente`), qui sont la contrainte réellement dimensionnante — la poutre I admet 9 000 daN alors que ses neuf points cumulés en admettent 17 000. L'optimiseur ne regarde jamais cela. Ce contrôle appartient au calcul charpente et à l'étude.

Source : `Shared/ScenarioHelpers/Models/Models.cs`, `Mother.Compute` et `Mother.Shares` ; `Shared/ScenarioHelpers/Core/Optimizer.cs`, `Build` (étapes 6 et 7) ; `Shared/LoadTransmissionHelper.cs`, `AddPalonnierSelfWeights` et `AddMotherGridSelfWeights`.

## Chapitre 10 — Ce qui est vérifié avant d'enregistrer, et ce qui est enregistré

Enregistrer une implantation n'ajoute rien à la base : cela **remplace**. Tous les `MotherGridPalonnier` du scénario sont supprimés, et avec eux toutes leurs `ChargeMp`, avant que les nouveaux soient insérés. La conséquence est brutale et il faut la poser d'emblée : une implantation à moitié bonne, enregistrée, détruit une implantation entière qui était bonne.

C'est pour cela que l'optimiseur ne se contente pas de calculer. Avant d'écrire quoi que ce soit, il pose deux questions, et une seule réponse fausse annule tout : *toutes les charges sont-elles portées ?* et *les points d'accroche portent-ils bien ce que pèsent les charges ?* Aucune des deux ne parle de sécurité de structure — elles parlent de cohérence. Un refus laisse la base exactement dans l'état où elle était, et les quatre messages de ce chapitre se terminent par la même phrase, qui est là pour rassurer l'utilisateur (les refus levés plus tôt — fichier d'inventaire manquant ou illisible — ne la portent pas, mais ne modifient rien non plus) :

```
L'implantation existante n'a pas été modifiée.
```

La page enveloppe le message de l'exception dans une seule alerte :

```
Génération impossible : <message>
```

### 10.1 Aucune charge oubliée

#### Pourquoi une charge peut rester à terre

L'optimiseur est glouton et il puise dans un stock fini. `SelectGreedy` choisit un groupe, retire ses pièces du `PieceStock`, recommence — et quand plus aucun groupe restant n'est assemblable avec ce qui reste, il sort de sa boucle (`if (best is null) break;`). Les charges de ces groupes-là ne sont portées par rien. De même, une ligne de mother qu'aucun modèle en stock ne peut porter est retirée, et les palonniers accrochés à ses extrémités sont retirés avec elle, leurs pièces rendues au stock.

Le résultat expose ces charges dans `RiggingResult.Uncovered` : celles dont le nom ne figure ni dans les charges d'un palonnier, ni dans les `DirectCharges` d'une ligne de mother.

La réponse naïve serait d'enregistrer ce qui a été placé et de prévenir. Elle est fausse ici, parce qu'enregistrer efface. Avec un stock vide, « ce qui a été placé » est *rien du tout* : l'utilisateur cliquerait sur « Générer l'implantation » et perdrait son implantation précédente en échange d'un scénario vide. La règle est donc tout ou rien.

```csharp
if (result.Uncovered.Count > 0)
    throw new InvalidOperationException(UncoveredMessage(result, pals, mothers));
```

#### Les deux refus qui précèdent

Avant même de calculer, `GenerateLayout` lit le plan de charges sur la page Charges (les lignes `ChargesScenique` du scénario, jamais `Charges.xlsx`) et le refuse s'il est vide :

```
Le scénario n'a aucune charge : la page Charges est vide. Ajoutez-y les charges, ou
importez-les avec le bouton « Import Excel », puis relancez la génération.
L'implantation existante n'a pas été modifiée.
```

*À faire :* saisir les charges sur la page Charges, ou les importer depuis `Charges.xlsx`.

Puis il vérifie chaque ligne, et rassemble **tous** les défauts avant de refuser — pas le premier rencontré :

```
Le plan de charges de la page Charges ne peut pas être placé : <défaut> ; <défaut> ; …
(+N). L'implantation existante n'a pas été modifiée.
```

Dix défauts au plus sont cités, les suivants comptés dans `(+N)`. Chaque défaut prend une de ces formes :

| Défaut | Texte |
|---|---|
| Nom vide | `la charge en X = 0 m, Y = 4,8 m (241 kg) : sans nom` |
| Nom en double | `« L11 » : deux charges portent ce nom` |
| Coordonnée manquante | `« L11 » : position incomplète (X = 0 m, Y = ? m)` |
| Poids manquant | `« L11 » : charge manquante` |
| Poids négatif | `« L11 » : charge négative (-40 kg)` |

Trois détails qui se voient dans le code et pas ailleurs. Une charge sans nom n'est pas désignée par un numéro — l'ordre de tri de l'optimiseur n'est pas celui de la page — mais par ce qu'elle a : sa position et son poids. Les coordonnées sont affichées **dans les axes des études** (`StudyAxes.StudyX(row.Y)`, `StudyAxes.StudyY(row.X)`), comme sur la page, et une coordonnée absente s'écrit `?`, jamais `0` : compléter par zéro placerait silencieusement la charge sur E5. Enfin **0 kg est accepté** : un point de charge qui ne pèse rien doit quand même être atteint par une structure, et l'étude le liste. Seuls l'absence de poids et un poids négatif sont refusés.

*À faire :* nommer les lignes sans nom, renommer les doublons, compléter X, Y et la charge. Les noms sont comparés sans tenir compte de la casse ni des espaces de bout : `l1` et `L1` sont le même nom.

#### Le refus « charges non portées »

`UncoveredMessage` construit une phrase par assemblage. Elle commence toujours par :

```
2 charge(s) sur 24 ne sont portées ni par un palonnier ni par un mother : S1 (550 kg), S2 (350 kg).
```

Huit noms au plus sont cités, les suivants comptés `(+N)`. Les poids sont écrits au format français (`1 200 kg`).

Viennent ensuite, seulement si elles s'appliquent, les phrases de diagnostic. Le diagnostic repose sur `LongestAssembly` : pour chaque modèle, on prend ses pièces en stock, les plus longues d'abord, **au plus 16** (`Assembler.MaxPieces`), et on additionne. C'est la plus longue barre que le fichier pourrait fabriquer, sans se demander ce qu'elle porterait.

```
Palonnier.xlsx n'a aucune pièce en stock.
MotherGrill.xlsx n'a aucune pièce en stock.
Même assemblé de toutes ses pièces, le plus long palonnier de Palonnier.xlsx mesure 3 m,
alors que deux lignes de points sont espacées d'au moins 3,6 m.
Même assemblé de toutes ses pièces, le plus long mother de MotherGrill.xlsx mesure 5,6 m,
alors que les poutres sont espacées de 7,14 à 7,7 m.
```

Les trois nombres de ces deux dernières phrases viennent de la géométrie réelle de la salle, et les seuils de comparaison sont exacts :

| Grandeur | Valeur | D'où |
|---|---|---|
| Poutres de la grille de l'optimiseur | I, H, G, F, E, D, C, B, A — neuf | `Geometry.Cols` |
| Écart entre deux poutres voisines | 7,144 m (E–D) à 7,700 m (H–G et C–B) | `Geometry.ColXList` |
| Écart entre deux lignes de points voisines | 3,600 m (8–7) à 4,101 m (9–8) | `Geometry.RowYList` |
| Tolérance de position TOL | 0,080 m | `Geometry.TOL` |
| Seuil « palonnier trop court » | 3,600 − 0,08 = **3,520 m** | `rowGaps.Min() - TOL` |
| Seuil « mother trop court » | 7,144 − 0,08 = **7,064 m** | `colGaps.Min() - TOL` |

> La poutre J existe dans la charpente et sur la figure des études, à 3,797 m de I, mais elle n'est **pas** dans la grille de l'optimiseur : `Geometry.Cols` va de I à A, et le § 12.1 dit pourquoi. Le 7,14 m du message est donc bien l'écart minimal entre deux poutres *utilisées*, pas l'écart minimal de la salle.

Vérifions sur l'inventaire réel du dossier partagé. Dans `Palonnier.xlsx`, le modèle « S500 Carrée Alu » a 24 pièces de 3 m et 23 de 2,8 m ; les 16 plus longues font 16 × 3 = **48 m**. 48 m est très au-dessus de 3,52 m : la phrase sur les palonniers ne sort pas. Dans `MotherGrill.xlsx`, « S500 Carrée Acier » a 39 pièces de 5,6 m ; les 16 plus longues font 16 × 5,6 = **89,6 m**, très au-dessus de 7,064 m : la phrase sur les mothers ne sort pas non plus. Avec ce parc, une charge non portée ne vient jamais d'un défaut de portée ; elle vient de la charge admissible, ou du nombre de pièces restantes.

> `LongestAssembly` lit `StockInit`, c'est-à-dire la colonne **Nombre** du fichier, pas ce qui reste après le placement. Ces deux phrases diagnostiquent le fichier, jamais la course de l'optimiseur. Une ligne à `Nombre = 0` ne compte pour aucune pièce : elle n'existe que pour donner une charge admissible.

Si le stock de mothers n'est pas vide, jusqu'à **trois** explications de lignes de mother invendables sont recopiées depuis `result.Errors` — celles qui commencent par `Ligne de mother`, écrites par `MotherLines.Explain` :

```
Ligne de mother G5–D5 : les pièces restant en stock ne permettent pas d'assembler 21,47 m
d'un modèle qui la porte.
Ligne de mother G5–D5 : 4 800 kg sur la travée F5–E5 (7,16 m), alors que le modèle le plus
fort en stock y admet 4 237 kg.
Ligne de mother G5–D5 : aucune pièce de MotherGrill.xlsx n'est en stock.
```

Enfin, si le réglage du chapitre 11 est actif :

```
Le réglage « orientations orthogonales uniquement » est activé : aucun palonnier incliné
n'a été proposé.
```

*À faire, dans l'ordre :* ajouter des pièces dans `MotherGrill.xlsx` ou `Palonnier.xlsx` (colonne Nombre) ; vérifier qu'un modèle assez fort existe pour la travée citée ; alléger ou déplacer la charge nommée ; en dernier recours, désactiver « orientations orthogonales uniquement » si la phrase apparaît.

#### Ce que ce garde-fou ne vérifie pas

`result.Errors` contient aussi les erreurs de capacité (`capErrors`) : un palonnier au-delà de sa valeur admise, une travée de mother au-delà de la sienne. **Ces erreurs n'empêchent pas l'enregistrement.** Seules deux conditions le font : `Uncovered` non vide, et `BalanceOk` faux. En pratique `capErrors` reste vide, parce que le dimensionnement ne choisit que des modèles qui portent — mais rien dans `Pipeline` ne le vérifie. De même, la charge en porte-à-faux au-delà des poutres extrêmes (`Mother.CantileverLoad`) n'est comparée à rien : aucune valeur d'abaque ne la couvre, et aucun refus ne la signale.

*Source : `Shared/ScenarioHelpers/Pipeline.cs`, `GenerateLayout` (les deux gardes) ; `Shared/ScenarioHelpers/Pipeline.Messages.cs`, `UncoveredMessage` ; `Shared/ScenarioHelpers/Core/MotherLines.cs`, `Explain`.*

### 10.2 Le bilan : ce que portent les points d'accroche

#### L'identité que le calcul doit satisfaire

Une charge posée sur un palonnier descend au mother par les réactions du palonnier ; le mother la descend aux poutres par bras de levier. À chaque étape la somme doit se conserver. Si, à l'arrivée, les points d'accroche portent moins que ce que pèsent les charges, quelque chose a fui : une charge comptée deux fois, une ligne oubliée, deux charges confondues sous un même nom.

```
BalanceOk  ⟺  | somme des charges aux points de grille − somme des charges portées | < 2,0 kg
```

Le seuil est écrit en dur : `Math.Abs(totalGrid - carriedTotal) < 2.0` (kg).

Le membre de gauche, `totalGrid`, est construit ainsi. Chaque palonnier répartit ses charges entre ses accroches : chaque charge va aux deux accroches qui l'encadrent, par bras de levier ; au-delà de la dernière, aux deux plus proches par la même règle affine — le porte-à-faux. Puis chaque ligne de mother répartit chacun de ses points entre les poutres : entre les deux qui l'encadrent, par bras de levier, et au-delà des extrémités par la même règle affine. Les parts d'un point somment toujours à 1 (`Mother.Shares`). Les résultats sont arrondis à trois décimales, cumulés par couple (poutre, ligne de points), et un cumul inférieur à 1e-6 kg est ignoré.

#### Un exemple qu'on peut refaire à la main

Prenons la charge **S3** du plan du propriétaire : 550 kg, en axes études X = 9 m, Y = −1 m, soit en axes de la grille (X = 1 ; Y = −9). Elle n'est pas à moins de 8 cm d'une ligne de points, donc elle va sur un palonnier, tendu entre les deux lignes qui l'encadrent : la ligne 8 (Y = −11,050) et la ligne 7 (Y = −7,450). Portée : 3,600 m — exactement le plus petit écart entre deux lignes de points de la salle.

```
A = (1 ; -11,050)   B = (1 ; -7,450)   portée 3,600 m
s (distance de S3 depuis A) = -9 - (-11,050) = 2,050 m

R_A = 550 x (3,600 - 2,050) / 3,600 = 550 x 1,550 / 3,600 = 236,8 kg
R_B = 550 x  2,050          / 3,600 = 313,2 kg          somme : 550,0 kg
```

Les deux lignes de mother passent par x = 1, entre les poutres E (x = 0) et D (x = 7,144) :

```
part de E = (7,144 - 1) / 7,144 = 0,860        part de D = 1 / 7,144 = 0,140

E8 = 236,8 x 0,860 = 203,6 kg      D8 = 236,8 x 0,140 =  33,2 kg
E7 = 313,2 x 0,860 = 269,4 kg      D7 = 313,2 x 0,140 =  43,8 kg

total : 203,6 + 33,2 + 269,4 + 43,8 = 550,0 kg
```

#### Ce que le bilan prouve, et ce qu'il ne peut pas prouver

Il prouve qu'aucun kilogramme ne s'est perdu ni dupliqué entre le plan de charges et les points d'accroche. C'est tout.

Reprenons l'exemple et déplaçons S3 jusqu'à l'extrémité B, sur la ligne 7. Alors `R_A = 0`, `R_B = 550`, et les points deviennent E7 = 473 kg, D7 = 77 kg, E8 = D8 = 0. Le total reste **550,0 kg**. Le bilan est donc **invariant à l'endroit où la charge tombe** : il ne dit rien de la répartition, rien du respect des abaques, rien de la pertinence du placement. Une implantation absurde mais close passe le bilan.

Deux autres limites, qu'il faut connaître pour ne pas mal lire le nombre affiché :

- Le total des points d'accroche **n'inclut pas le poids propre des structures**. Dans `Mother.Compute`, seule la composante `F` d'un point de suspension est transmise aux poutres ; la composante `W`, la part du poids propre du palonnier, en est exclue. L'étude, elle, ajoute le pré-rigg aux charges scéniques. « Total aux points d'accroche » de l'optimiseur n'est donc pas le total de l'étude.
- Le membre de droite est la somme des charges **portées** (`carriedTotal`), pas des charges du plan. Le message de refus, lui, affiche `result.TotalCharges`, qui est la somme de *toutes* les charges. Les deux ne coïncident que parce que le garde-fou du § 10.1 est passé avant : à ce point-là, plus aucune charge n'est non portée.

Le seuil de 2,0 kg est énorme devant les arrondis : quelques centaines de points arrondis au gramme ne font pas 0,1 kg d'écart. Il n'est pas là pour absorber du bruit numérique, mais pour attraper une fuite structurelle — par exemple deux charges portant le même nom, que l'optimiseur, la `ChargeMp` stockée et l'étude confondraient toutes les trois. Or ce cas-là est déjà refusé par le contrôle du plan. **Ce garde-fou ne devrait donc jamais se déclencher ; s'il se déclenche, c'est un défaut du logiciel, pas une erreur de saisie.**

Le message est :

```
Les points d'accroche portent 5910 kg pour 6210 kg de charges : l'implantation calculée
n'est pas cohérente et n'a pas été enregistrée. L'implantation existante n'a pas été modifiée.
```

*À faire :* vérifier qu'aucun nom de charge n'est en double sur la page Charges, puis signaler le cas avec la sortie console de la génération — elle imprime, ligne par ligne, chaque point de grille, `Total charges`, `Total points grille` et `❌ Déséquilibre: … kg`.

*Source : `Shared/ScenarioHelpers/Core/Optimizer.cs`, `Build` (étapes 6 et 7) et `SupportReactions` ; `Shared/ScenarioHelpers/Models/Models.cs`, `Mother.Compute` et `Mother.Shares` ; `Shared/ScenarioHelpers/Pipeline.cs`, `GenerateLayout`.*

### 10.3 Le remplacement en une seule transaction

#### Le problème

Le remplacement se fait en deux temps : supprimer, puis insérer. Entre les deux, le scénario est vide. Si la seconde moitié échoue — une contrainte violée, une coupure réseau, un délai dépassé — l'utilisateur se retrouve avec un scénario sans aucune structure, sans avoir rien demandé de tel. C'est le pire résultat possible : pire qu'un refus, pire qu'une implantation médiocre.

#### La règle

Tout le remplacement est une seule transaction, écrite dans `LayoutPersistence.ReplaceElementsAsync`, sur le contexte que la génération possède (§ 3.1). Elle se lit de haut en bas :

1. `db.ChangeTracker.Clear()` — repartir d'un suivi propre.
2. `BeginTransactionAsync`.
3. Lire les `MotherGridPalonnier` du scénario ; compter `removed`.
4. Supprimer d'abord les `ChargeMp` qui pointent vers eux (clé étrangère), puis les éléments ; `SaveChangesAsync`.
5. Ajouter les nouveaux éléments et les nouvelles `ChargeMp` ; `SaveChangesAsync`.
6. `CommitAsync`.

Le tout est exécuté à l'intérieur d'une **stratégie d'exécution** (`db.Database.CreateExecutionStrategy()`), et ce n'est pas décoratif. Le contexte est configuré avec `EnableRetryOnFailure` (`Startup.cs`) : EF Core rejoue une opération après une panne transitoire. Une telle stratégie **refuse** toute requête dans une transaction ouverte en dehors d'elle. En plaçant le bloc entier dans `strategy.ExecuteAsync`, une nouvelle tentative rejoue *tout* — nettoyage du suivi compris — et une tentative ratée ne laisse rien derrière elle.

En cas d'échec, le `catch` vide le suivi (`db.ChangeTracker.Clear()`) et relance. Ce nettoyage protégeait les pages du temps où le contexte était le leur. Il ne protège plus personne : `GenerateLayout` possède ce contexte et le libère en sortant, quoi qu'il arrive. Il ne coûte rien et il n'a pas été retiré — mais ne comptez sur lui pour rien d'autre que laisser un contexte propre avant qu'il ne disparaisse. Le `ChangeTracker.Clear()` de l'étape 1, lui, est indispensable : c'est celui que la stratégie de reprise rejoue avant chaque nouvelle tentative.

**Ce que l'utilisateur garde quand cela échoue :** son implantation précédente, entière — éléments, `ChargeMp`, accrochages posés à la main, compositions. La transaction a été annulée ; rien n'a été supprimé.

Quand cela réussit, la console dit ce qui a été échangé :

```
  37 élément(s) et 24 ChargeMp existant(s) remplacé(s) par 6 Palonnier(s), 7 ligne(s) de mother et 24 ChargeMp.
  mother et 24 ChargeMp.
```

#### Trois choses qui ne sont pas dans la transaction

> Le remplacement supprime **tous** les `MotherGridPalonnier` du scénario, y compris ceux dessinés à la main sur la page MotherGrid ou importés par `Elements.xlsx`, et y compris les accrochages modifiés à la main (les enregistrements `Manual` de `Accrochages`). Régénérer une implantation, c'est accepter de perdre ce travail-là. Rien ne le demande à l'utilisateur.

Les `ElementType` créés à partir des modèles de l'inventaire (§ 2 de `GenerateLayout`) sont enregistrés **avant**, par leur propre `SaveChanges`, hors transaction : un refus ultérieur les laisse en base. Ils sont inoffensifs et réutilisés à la génération suivante.

Le plan PNG (`rigging_plan_v5.png`) et le rapport Excel (`rigging_report_v5.xlsx`) sont écrits **après** le commit. Deux conséquences opposées à retenir : si le dessin ou l'export échoue, l'implantation est déjà en base alors que les fichiers de sortie manquent ou sont périmés ; et si la génération est refusée plus tôt, le dossier de sortie conserve le plan et le rapport de la génération *précédente*, qui ne correspondent plus à rien de nouveau. Ni l'un ni l'autre n'est signalé.

Enfin, ce qui protège les saisies en cours des autres pages n'est plus un nettoyage mais une **séparation** : `GenerateLayout` ouvre son propre contexte (`EntitiesService.CreateOwnedContext`), écrit tout par lui et le libère à la fin. Le contexte partagé du circuit, avec la ligne de la page Charges que quelqu'un est en train de taper, n'est jamais touché — ni lu, ni enregistré, ni vidé. L'appel `entitiesService._appDBContext.ChangeTracker.Clear()` qui ouvrait autrefois `GenerateLayout` a disparu avec la raison qui le justifiait.

*Source : `Shared/ScenarioHelpers/LayoutPersistence.cs`, `ReplaceElementsAsync` ; `Shared/ScenarioHelpers/Pipeline.cs`, l'ouverture du contexte dans `GenerateLayout` ; `Shared/Data/EntitiesService.cs`, `CreateOwnedContext` ; `AliantWebS/Startup.cs` pour `EnableRetryOnFailure`.*

### 10.4 Ce que la base garde de l'implantation

#### Deux tables, et rien d'autre

Tout ce qui survit d'un calcul d'implantation tient dans deux tables : `MotherGridPalonnier`, un enregistrement par ligne de mother et par palonnier, et `ChargeMp`, un enregistrement par charge posée sur l'un d'eux. Les deux listes sont construites par des fonctions **pures** — `BuildElementEntities` et `BuildChargeMps` — que l'on peut donc vérifier sans base de données. L'ordre est le contrat entre elles : `BuildChargeMps` retrouve l'élément porteur par son index dans `result.Palonniers` et `result.Mothers`.

| Colonne | Ligne de mother (`Famille` = true) | Palonnier (`Famille` = false) |
|---|---|---|
| `Name` | `S500 Carrée Acier L=22,4m #1` | `F34200 L=4,0m #3` |
| `Famille` | `true` | `false` |
| `Numero` | jamais renseigné (null) | jamais renseigné (null) |
| `Type` | jamais renseigné (null) | jamais renseigné (null) |
| `X1`, `Y1` | extrémité gauche dessinée : `XL` et le Y de la ligne de points | extrémité A du palonnier |
| `X2`, `Y2` | extrémité droite : `XR`, même Y | extrémité B |
| `MotherAID` | null | la ligne de mother sous l'extrémité A |
| `MotherBID` | null | la ligne de mother sous l'extrémité B |
| `Accrochages` | null | JSON des croisements tranchés |
| `Composition` | `[560,560,560,560]` | `[200,200]` |
| `ElementTypeID` | le modèle, dans la catégorie mothergridpalonnier | idem |
| `EventMPScenarioID` | le scénario | le scénario |

Le nom est composé, pas choisi : `$"{Bar.Type} L={Bar.Length:F1}m #{i + 1}"`, où la longueur est celle de l'assemblage — la somme de ses pièces — et non la portée franchie. Le séparateur décimal suit la culture du serveur.

Les quatre coordonnées sont **dans les axes de la grille** : X en travers des poutres vers A, Y le long des poutres vers le point 1. Les pages, le plan, l'étude et les exports lisent les propriétés non mappées `StudyX1`, `StudyY1`, `StudyX2`, `StudyY2`, qui appliquent la réflexion (x, y) → (−y, −x) de `Helpers.StudyAxes`. Une ligne de mother a donc `Y1 = Y2` en base (elle est en travers des poutres) et un `StudyX` constant ; un palonnier le long des poutres a `X1 = X2` en base et un `StudyY` constant.

#### Les trois colonnes qui portent une décision

`MotherAID` et `MotherBID` nomment les deux lignes de mother sous les extrémités du palonnier. Elles sont associées **par identité d'objet**, pas par géométrie : quand un mother est plein, l'optimiseur en empile un second dans la même travée, et les deux sont alors indiscernables sur le plan. Ce sont volontairement des colonnes sans clé étrangère — le remplacement supprime et réinsère tout d'un bloc, SQL Server refuse les auto-références en cascade, et le calcul n'utilise un identifiant que s'il désigne un mother que le palonnier rencontre encore.

`Accrochages` est le JSON des croisements. Un palonnier pend par défaut de **toutes** les lignes de mother qu'il croise ; cette colonne enregistre ce que quelqu'un a décidé, un enregistrement par mother : `m` l'identifiant, `h` accroché ou non, `o` l'origine (1 = optimiseur, 2 = à la main), `r` la raison. L'optimiseur écrit un enregistrement par accroche retenue et un par croisement écarté, avec sa raison, rédigée au passé parce que les charges peuvent changer ensuite :

```
au placement, une travée du mother « S500 Carrée Acier L=22,4m #1 » aurait porté 4 800 kg
pour 4 237 kg admissibles
```

Quand la travée surchargée est celle du mother écarté lui-même, la raison s'abrège en `au placement, une travée du mother aurait porté …`. Une main qui accroche ou décroche ajoute un enregistrement d'origine 2 **à côté** de celui de l'optimiseur, sans l'effacer : annuler les retouches manuelles rend la décision de l'optimiseur.

`Composition` est la liste des pièces, en centimètres entiers, la plus longue d'abord : `[560,560,560,560]` pour quatre pièces de 5,6 m. Format sans culture ni bruit flottant, colonne de 400 caractères, seize pièces au plus. `null` signifie un élément placé avant que les pièces soient enregistrées : sa longueur est alors relue dans son nom.

#### Les charges enregistrées

| Colonne de `ChargeMp` | Contenu |
|---|---|
| `Name` | le nom de la charge, tel qu'il est sur la page Charges |
| `Numero` | 1, 2, 3 … dans l'ordre des charges **de cet élément** |
| `X` | la distance en mètres depuis (X1, Y1), **le long de l'élément** |
| `Charge` | le poids en kg |
| `MotherGridPalonnierID` | l'élément porteur |

> `ChargeMp.X` n'est pas une coordonnée. C'est une abscisse curviligne, en mètres, mesurée depuis l'extrémité (X1, Y1) de l'élément. Il n'y a pas de `Y` : la position transversale n'existe pas, la charge est sur la barre. `LoadTransmissionHelper` s'en sert directement pour trouver les deux accroches qui encadrent la charge.

Pour un palonnier, `X` est la projection de la charge sur l'axe A→B. Reprenons S3, 550 kg, sur le palonnier A = (1 ; −11,050), B = (1 ; −7,450) : `X = −9 − (−11,050) = 2,050 m`, `Numero = 1`.

Pour une charge pendue directement sur une ligne de mother, `X = OnLine(m, c.X) − m.XL`, c'est-à-dire la distance depuis l'extrémité gauche dessinée, après avoir ramené la charge sur le segment (une charge quelques centimètres au-delà d'un bout est sur ce bout). Exemple : la charge **L14** du plan du propriétaire, en axes études (0 ; 8,23), est en axes grille (−8,23 ; 0), donc sur la ligne de points 5. Le mother de cette ligne s'étend de la poutre G (x = −14,406) à la poutre D (x = 7,144), soit 21,550 m. On stocke `X = −8,23 − (−14,406) = 6,176 m`.

#### Ce qui n'est pas enregistré

Tout ce que l'optimiseur a calculé pour décider disparaît à l'enregistrement : `GridLoads` (les charges aux points d'accroche), les charges travée par travée, les valeurs admissibles lues dans les abaques, les réactions `RA` et `RB`, les poids propres répartis, le stock restant, la liste `Errors`, l'option `OrthogonalOnly` — et, depuis les chapitres 15 et 16, le coût mesuré (`RiggingResult.Cost`), le plancher théorique (`Bound`) et ce que la recherche a fait (`Search` : classement appliqué, niveau, optimum prouvé ou non). Ces chiffres ne vivent que dans le plan PNG, le rapport Excel du dossier de sortie et la sortie console — et chacun n'est pas partout : le coût et le plancher sont dans la feuille *Synthèse* et sur la console, la ligne de recherche sur la console seule, aucun des trois sur le plan PNG. La base ne garde que la **géométrie** et les **décisions d'accrochage** ; « Calcul Charpente » recalcule tout le reste à partir d'elles, en suivant exactement les mêmes règles — c'est pour cela que l'optimiseur dimensionne chaque travée sur ce que le calcul y trouvera.

Deux colonnes existantes ne sont jamais remplies par le placement, alors que les pages les affichent : `Numero` et `Type` de `MotherGridPalonnier`. Un lecteur qui trie les éléments par `Numero` après une génération triera sur des nulls.

Deux causes d'échec à l'insertion, enfin, qui feront perdre toute la transaction (§ 10.3) :

- `ElementTypeId = et?.ElementTypeId ?? Guid.Empty` : si un modèle de l'inventaire n'a pas de `ElementType` correspondant dans la catégorie mothergridpalonnier — typiquement quand cette catégorie est absente, ce que la console signale par `⚠️ Catégorie 'mothergridpalonnier' introuvable — ElementTypes non créés.` — la clé étrangère `FK_MotherGridPalonnier_ElementType` refuse la ligne.
- `Name` est limité à 50 caractères par la colonne, et rien ne tronque le nom composé. Un modèle dont le nom dépasse une trentaine de caractères produit un nom trop long.

*Source : `Shared/ScenarioHelpers/Pipeline.Entities.cs`, `BuildElementEntities` et `BuildChargeMps` ; `Shared/Data/Models/MotherGridPalonnier.cs`, `Shared/Data/Models/MotherGridPalonnierStudyAxes.cs` et `Shared/Data/Models/ChargeMp.cs` ; `Shared/PalonnierHanging.cs`, `PalonnierHooks` ; `Shared/TrussComposition.cs`.*

## Chapitre 11 — Les réglages : orientations, classement, niveau de recherche

Trois réglages seulement changent ce que l'optimiseur produit, et les trois vivent dans la table
`AppSetting` : une seule valeur pour toute l'application, jamais par scénario ni par utilisateur. Le premier
est l'objet des deux sections qui suivent ; les deux autres, plus récents, sont au § 11.3.

### 11.1 « Orientations orthogonales uniquement » : ce qu'il change

#### Une seule ligne de comportement

Le réglage vit dans la table `AppSetting`, sous la clé `optimizer.orthogonalOnly`, et vaut `false` par défaut. `GenerateLayout` le lit une fois par génération :

```csharp
bool orthogonalOnly = await AppSettings.GetBoolAsync(AppSettings.OptimizerOrthogonalOnly, false);
```

Il est ensuite porté dans `OptimizerOptions.OrthogonalOnly`, et il n'a qu'un seul effet réel dans tout le moteur :

```csharp
var allGroups = GroupGenerator.Generate(toHang, Feasible, allowBiais: !options.OrthogonalOnly);
```

Ce qui disparaît, ce sont les **groupes biais** de `GroupGenerator` : les groupes de 2 à 4 charges alignées sur une droite qui n'est ni parallèle aux poutres ni perpendiculaire à elles. Un candidat biais exige que l'étalement des charges dépasse 0,08 m (`Geometry.TOL`) dans les deux directions, et, à partir de trois charges, que chacune soit à moins de 0,20 m (`Geometry.ColinearTol`) de l'axe reliant la plus basse à la plus haute en Y.

Un effet secondaire, dans `SelectGreedy` : la règle 2 de `PreferGroup` — à nombre de charges égal, le palonnier incliné — ne peut plus jamais s'appliquer, faute de candidat incliné. La sélection se réduit à « le plus de charges d'abord, puis la plus courte portée » (§ 7.4). Les deux constantes qui portaient autrefois cette préférence, un `+0,1` dans le tri et un `+0,3` dans le score, n'existent plus nulle part.

#### Ce qu'il ne change pas

Les lignes de mother, leur planification travée par travée, leur dimensionnement, les charges pendues directement sur une ligne de points, l'accrochage à toutes les lignes croisées, la lecture des abaques, le contenu enregistré : rien de tout cela ne dépend du réglage. En particulier, **une implantation déjà enregistrée n'est pas touchée** quand on change le réglage — et rien, dans la base, ne dit avec quel réglage elle a été produite. Seuls le plan PNG, le rapport Excel et la sortie console portent la trace :

| Où | Trace |
|---|---|
| Console | `  Orientations orthogonales uniquement : aucun palonnier incliné.` ou `  Palonniers inclinés autorisés.` |
| Plan PNG | `· orientations orthogonales uniquement` ajouté à la deuxième ligne du bandeau |
| Rapport Excel | la ligne `Orientations orthogonales uniquement` : `oui` ou `non` |
| Message de refus | la phrase du § 10.1, quand des charges ne sont pas portées |

#### Comment il se règle

Sur la page Paramètres, section « Layout Optimiser », visible aux seuls rôles **Administrator** et **Developer** — le gestionnaire du clic revérifie le rôle, il ne se fie pas au fait que la section soit cachée. Le libellé annonce la portée sans ambiguïté : le réglage « Applies to everyone ». Il n'y a **pas** de réglage par scénario, ni par événement : une seule ligne dans `AppSetting` pour toute l'application.

Le basculement est prudent. `AppSettings.SetAsync` journalise ses échecs au lieu de lever une exception ; la page relit donc la valeur en donnant comme repli **le contraire** de ce qui a été demandé, pour qu'une table illisible ne puisse pas passer pour une écriture réussie. Si la relecture ne rend pas la valeur voulue, l'utilisateur voit l'alerte `The setting could not be saved.` et rien n'est journalisé. Sinon, une entrée est écrite au journal, catégorie Configuration, clé `optimizer.orthogonalOnly`, sévérité 1 : `Activé : Generate Layout ne propose aucun palonnier incliné` ou `Désactivé : Generate Layout peut proposer des palonniers inclinés`.

> La table `AppSetting` est entièrement mise en cache en mémoire, sans durée de vie ; seule une écriture vide le cache, et seulement dans le processus qui écrit. Par ailleurs, `GetBoolAsync` ne lève jamais : base injoignable, table absente ou valeur illisible, il rend le repli — ici `false`, donc **palonniers inclinés autorisés**. Un réglage actif peut donc se retrouver silencieusement inactif si la lecture de la table échoue. Les valeurs acceptées sont `true`, `false`, `1` et `yes`.

*Source : `Shared/Data/AppSettings.cs` (clé `OptimizerOrthogonalOnly`, `GetBoolAsync`, `SetAsync`) ; `AliantWebS/Pages/Settings.razor`, `ToggleOrthogonalOnlyAsync` ; `Shared/ScenarioHelpers/OptimizerSettings.cs`, `ReadAsync` et `Describe` ; `Shared/ScenarioHelpers/Core/GroupGenerator.cs`, `Generate`.*

### 11.2 « Orientations orthogonales uniquement » : pourquoi il existe

#### Ce que font les six études écrites à la main

Les six études d'implantation existantes de l'Antarès partagent une même grammaire, et le moteur a été aligné sur elle :

- les mothers sont des **lignes continues** au-dessus d'une ligne de points, en travers des poutres, sur 2 à 4 travées, accrochées à chaque poutre traversée ;
- elles sont assemblées de pièces de 5,6 m, 2,8 m et 1,4 m d'un même modèle ;
- un moteur à moins de 8 cm d'une ligne de points pend **directement** sur son mother, sans palonnier ;
- les palonniers courent **le long des poutres** ;
- chaque palonnier pend de toutes les lignes de mother qu'il croise, et chaque charge va aux deux accroches qui l'encadrent ;
- chaque portée entre accroches voisines est comparée à la charge ponctuelle au milieu du modèle, lue à la longueur immédiatement supérieure.

Le point qui nous occupe : **aucune de ces six études n'emploie de palonnier incliné.** Le réglage existe pour que la machine produise des plans que le rigger de la salle reconnaît, et qu'un vérificateur puisse rapprocher d'une étude existante ligne à ligne. C'est une contrainte de méthode, pas de résistance.

Il est désactivé par défaut parce qu'un palonnier incliné est parfois plus efficace : il rassemble jusqu'à quatre charges alignées en biais sur une seule barre, là où l'orthogonal en demanderait quatre. Le moteur est donc autorisé à le proposer tant que personne ne l'interdit.

#### Ce que l'option coûte en couverture

Elle ne retire jamais le **dernier** candidat d'une charge. Chaque charge à suspendre forme toujours, à elle seule, un groupe Y : un palonnier à son abscisse, tendu de la ligne de points en dessous à celle au-dessus. L'option ne supprime que les candidats qui **regroupent** des charges.

Le coût est donc indirect, et il est en pièces : plus de palonniers, donc plus de pièces prises au `PieceStock`, donc plus de chances qu'une charge tardive ne trouve plus rien à assembler — et une charge non portée fait refuser toute la génération (§ 10.1). C'est précisément pourquoi `UncoveredMessage` ajoute sa phrase quand l'option est active : c'est la première chose à essayer de relâcher.

#### Sur le plan réel du propriétaire, l'option ne change rien

Le plan de 24 charges du dossier partagé `_common/data` pèse 6 210 kg. Vérifions ce que l'option y fait.

Dix-huit charges (L1 à L15, L18, L19, L20) ont un X d'étude de −7,45 m, 0 m ou 7,4 m, soit un Y de grille de 7,45, 0 ou −7,4 m. Les lignes de points 3, 5 et 7 sont à 7,400, 0 et −7,450 m : l'écart est de 0,05 m au plus, sous les 0,08 m de `Geometry.TOL`. Ces dix-huit charges pendent **directement sur les mothers**, sans palonnier — l'option ne les concerne pas.

Restent six charges à suspendre : L16, L17, S1, S2, S3, S4. Les candidats biais qu'elles pourraient former sont tous très longs, parce que la droite qui joint deux d'entre elles est prolongée jusqu'aux deux lignes de points qui encadrent le groupe. Pour la paire la plus resserrée, L16 et S1 :

```
L16 (grille) = (-8,98 ;  1,02)      S1 (grille) = (1,00 ; 9,00)
lignes encadrantes : ligne 5 (y = 0) et ligne 2 (y = 11,05)

xA = -8,98 + (0 - 1,02) / 7,98 x 9,98      = -10,26
xB = -8,98 + (11,05 - 1,02) / 7,98 x 9,98  =   3,56
portée = racine( 13,82² + 11,05² ) = 17,7 m
```

Or l'inventaire de palonniers a pour plus grande longueur de ligne 3 m, et une charge admissible n'est jamais calculée au-delà de **deux fois** la plus grande longueur d'un modèle (`LoadTable.ExtrapolationLimit = 2,0`). Aucun palonnier ne peut donc être vérifié au-delà de 6,01 m. Le test de faisabilité rejette 17,7 m, et rejette de même toutes les autres paires en biais de ce plan, qui sont plus longues encore. Il rejette aussi le groupe Y {S1, S3}, qui demanderait 22,1 m.

Il ne reste que six groupes d'une charge, de portées 3,60 m (S3, S4), 3,65 m (S1, S2), 3,70 m (L16) et 3,75 m (L17). **Avec ou sans le réglage, ce plan donne exactement les mêmes six palonniers.** L'option ne se fait sentir que sur un plan où des charges s'alignent en diagonale à quelques mètres l'une de l'autre — assez près pour qu'un biais court soit assemblable et vérifiable.

*Source : `Shared/ScenarioHelpers/Core/GroupGenerator.cs`, `GenerateBiaisGroups` et `WithinAxis` ; `Shared/ScenarioHelpers/Core/Optimizer.cs`, `SelectGreedy` et `AssemblyFor` ; `Shared/ScenarioHelpers/LoadTable.cs`, `At`.*

### 11.3 Les deux réglages du développeur : le classement et le niveau de recherche

Deux réglages sont arrivés avec les chapitres 15 et 16. Ils ne touchent pas à la géométrie : le premier dit
ce que l'optimiseur préfère, le second combien de temps il le cherche.

#### `optimizer.objective.order` — le classement des critères

La valeur est la liste des **clés** des critères, séparées par des virgules, dans l'ordre voulu, le premier
critère d'abord. La valeur par défaut, celle qui s'applique quand rien n'est enregistré, est :

```
palonniers,masse,pieces,longueur,marge
```

Cinq clés, chacune exactement une fois. Ce sont des clés, pas des libellés : le libellé français d'un
critère peut être réécrit le jour où un rigger demande mieux, la clé non — elle est dans la base de chaque
installation, et une ligne écrite le mois dernier doit encore se relire comme le même classement. Le
chapitre 15 dit ce que chaque critère mesure et pourquoi l'ordre est lexicographique.

Sur la page Paramètres, le développeur ne tape pas cette chaîne : il monte et descend les critères ligne à
ligne, et la page écrit la liste. Un bouton « Restore the default order » remet les cinq clés dans l'ordre
du code.

#### `optimizer.searchLevel` — combien l'optimiseur cherche

Deux valeurs, et rien d'autre — ce sont les noms des membres de `SearchLevel`, lus sans distinction de
casse par `Enum.TryParse` :

| Valeur | Ce qui tourne | Ce qu'on obtient |
|---|---|---|
| `bounds` (défaut) | le placement glouton, plus le plancher théorique calculé à côté | la réponse immédiate, et de combien elle pourrait être au-dessus du plancher |
| `exact` | le glouton d'abord, puis toutes les façons de grouper les charges qui demandent un palonnier | la meilleure implantation au sens du classement, et la mention « optimum prouvé » quand tout a été parcouru |

`bounds` est ce que faisait chaque génération avant que le réglage n'existe. `exact` ne peut jamais rendre
une implantation que le classement juge moins bonne que celle du glouton — c'est la garantie du chapitre 16
—, mais il prend plus de temps, et il s'arrête sur son budget (2 000 ms, 20 000 nœuds) plutôt que de faire
attendre une page.

#### Une valeur illisible ne change jamais ce que l'optimiseur préfère

C'est la même règle pour les deux, et elle est écrite une fois, dans `OptimizerSettings.ReadAsync` :
**une valeur que personne ne peut lire retombe sur le défaut, et le dit.**

- Classement (`Objective.Parse`) : une clé inconnue, une clé répétée, ou une clé manquante rendent la
  valeur inutilisable. Le classement par défaut s'applique **en entier** — jamais un ordre partiel
  rafistolé à coups de suppositions — et une phrase française part dans la console et remonte à
  l'appelant : « Le classement enregistré est illisible : … Le classement par défaut est appliqué
  (palonniers,masse,pieces,longueur,marge). » Rien d'enregistré n'est pas un problème et ne dit rien :
  c'est le défaut, qui est précisément ce que l'appelant reçoit.
- Niveau de recherche : tout ce qui n'est ni `bounds` ni `exact` donne `bounds`, avec la phrase « Le niveau
  de recherche « … » n'est pas reconnu : la recherche rapide a été utilisée. »

La page Paramètres applique la même prudence dans l'autre sens : elle réécrit la valeur, la **relit**, et
n'affiche comme choisi que ce que la table contient réellement — `AppSettings.SetAsync` journalise ses
échecs au lieu de lever. Si la relecture ne rend pas la valeur voulue, l'utilisateur voit
`The setting could not be saved.` et rien n'est journalisé. Quand une valeur illisible est déjà dans la
table, la carte l'affiche telle quelle, entre guillemets, et dit lequel des deux réglages est réellement
en vigueur.

#### Qui peut les changer, et sur qui ils agissent

Les deux cartes sont dans la section « Developer Tools » de la page Paramètres et ne sont montrées qu'au
rôle **Developer** — le réglage orthogonal, lui, reste visible aux rôles Administrator et Developer
(§ 11.1). Le rôle est **revérifié dans le gestionnaire** avant chaque écriture (`IsDeveloperNowAsync`) :
cacher une carte n'est pas une permission.

Et ils s'appliquent à **tout le monde**. C'est écrit sur les deux cartes : « Applies to everyone: one layout
engine, one answer. Only a developer can change it. » Il n'y a qu'un moteur d'implantation et une réponse
par plan ; ce ne sont donc pas des préférences d'affichage, ce sont les règles que la salle reçoit. Chaque
changement part au journal, catégorie Configuration, sévérité 1, sous la clé du réglage.

Enfin, chaque exécution garde la trace de ce qui l'a produite : `RiggingResult.Search` porte le classement
appliqué sous sa forme enregistrée, le niveau, le nombre d'implantations examinées, le temps et la mention
d'optimalité. La console l'imprime en fin de génération, juste après le coût mesuré et le plancher. Cette
trace n'est **pas** enregistrée en base (§ 10.4), et elle n'est pas non plus dans le classeur : le classeur
porte le coût et le plancher, la ligne de recherche ne vit que sur la console.

*Source : `Shared/Data/AppSettings.cs` (clés `OptimizerObjectiveOrder` et `OptimizerSearchLevel`) ;
`Shared/ScenarioHelpers/OptimizerSettings.cs`, `ReadAsync` et `Describe` ;
`Shared/ScenarioHelpers/Core/Objective.cs`, `Parse` et `Format` ;
`Shared/ScenarioHelpers/Models/Models.cs`, `SearchLevel`, `OptimizerOptions` et `SearchOutcome` ;
`AliantWebS/Pages/Settings.razor`, `SaveObjectiveOrderAsync`, `ReadSearchLevelAsync`, `SetSearchLevelAsync`
et `IsDeveloperNowAsync`.*

## Chapitre 12 — Limites connues, pièges et idées reçues

Un outil de calcul ne devient sûr que lorsque l'on sait ce qu'il ne fait pas. Les trois sections qui suivent
n'ajoutent aucune règle : elles délimitent celles des chapitres précédents. Un lecteur qui n'aurait que ce
chapitre doit au moins retenir ceci : **l'optimiseur dit maintenant ce qu'il cherche — les chapitres 15 et 16
l'écrivent noir sur blanc — mais dans son réglage par défaut il ne le cherche pas jusqu'au bout.** Il place
avec un algorithme glouton qui ne revient jamais sur ses pas, et se contente d'annoncer, à côté du résultat,
le plancher que personne ne peut passer.

### 12.1 Ce que l'optimiseur ne cherche pas

#### Par défaut, il ne minimise toujours rien de global

Le mot « optimiseur » a longtemps été trompeur pour une autre raison : jusqu'à peu, rien dans le code ne
disait ce qu'il optimisait. Ce n'est plus le cas. Les quatre contraintes dures et le classement des cinq
critères sont écrits, nommés et réglables (`Objective.cs`, chapitre 15), et chaque implantation terminée est
mesurée une fois, quantité par quantité (`LayoutCost`).

Ce qui reste vrai, c'est que le **placement par défaut** ne parcourt pas les autres réponses. Ouvrez
`SelectGreedy` : à chaque tour, la fonction choisit **un** groupe de charges — le premier rencontré de la
meilleure classe au sens de `PreferGroup` (§ 7.4) — et ne le remet jamais en cause. Ses pièces sont
immédiatement sorties du stock (`stock.Take`), et le tour suivant ne voit plus que ce qui reste.

C'est l'heuristique classique « le plus gros ensemble d'abord » du problème de couverture. Elle est rapide, et
elle n'est pas optimale : il existe des plans où une autre répartition emploie moins de palonniers, moins de
pièces ou charge moins la charpente. Au niveau `bounds`, le code ne les cherche pas — il dit seulement de
combien la réponse pourrait, au plus, être au-dessus du plancher (chapitre 16).

Ce qu'il faut donc retenir, réglage par réglage :

- **`bounds`, le défaut** : une seule implantation est construite. Aucune des cinq quantités du classement
  n'est minimisée ; elles sont seulement **mesurées**, et comparées à un plancher.
- **`exact`** : les groupements sont parcourus, et la meilleure implantation au sens du classement est
  gardée. Là, et seulement là, le premier critère du classement est réellement minimisé — parmi les
  couvertures de ces charges par ces groupes candidats, et rien au-delà (§ 16.5).

Et voici ce que **personne** ne doit lui attribuer, dans aucun des deux modes : la charge aux points
d'accroche, la charge sur une poutre, le coût, le temps de montage, le nombre de moteurs. Aucune de ces
quantités n'entre dans le classement.

#### Les deux constantes qui portaient le biais ont disparu

Elles méritent une ligne, parce qu'elles ont longtemps été la seule réponse à la question « qu'est-ce que
l'optimiseur préfère ? ». Le tri des candidats ajoutait `0,1` à un groupe incliné, et le score, dans la
boucle, lui en ajoutait `0,3`. Les deux nombres n'étaient pas les mêmes, ils ne pouvaient pas être expliqués,
et le premier ne pouvait de toute façon jamais changer un résultat. Ils sont remplacés par une comparaison
écrite en toutes lettres, `Optimizer.PreferGroup` : le plus de charges, puis l'incliné, puis la plus courte
portée. **Le comportement est identique** — c'est la condition qu'on s'était fixée pour les retirer.

#### Il ne regarde jamais le cahier des charges de la salle

L'optimiseur vérifie les ponts, et **rien d'autre**. Il compare la charge d'une travée à la charge ponctuelle
au milieu du modèle, et la charge d'un palonnier à sa valeur sur sa portée. La capacité d'un point d'accroche
et la capacité totale d'une poutre — la table `TCharpente`, qui est la contrainte dimensionnante — ne sont
lues nulle part dans `Optimizer.cs`. Une implantation peut être « dans les valeurs admises » sur tous ses
ponts et dépasser la capacité d'une poutre : c'est l'étude Word, et elle seule, qui le dit.

#### La poutre J n'existe pas pour lui, et on sait maintenant pourquoi

`Geometry.Cols` vaut `{ "I","H","G","F","E","D","C","B","A" }` — neuf poutres. La poutre J, bien présente dans
`CharpenteCoordinates` et dessinée sur le plan, **n'est pas dans la grille de l'optimiseur**. Ce manuel a
longtemps signalé cette absence sans pouvoir l'expliquer. Elle est expliquée, et la raison est celle de tout
le reste du moteur : **l'optimiseur implante la grille que les études implantent.**

Les six études d'implantation écrites à la main disent la même chose, six fois :

- chacune cite J **une seule fois**, au § 2.3, celui du cahier des charges ;
- ce qu'elle y admet : **500 daN un point sur deux** — J1, J3, J5, J7, J9 —, **zéro** aux autres, et
  **2 500 daN** pour la poutre entière, contre **9 000 à 13 500 daN** pour les autres poutres ;
- **aucune des six** ne fait figurer J dans son tableau « Charge par point d'accroche ».

S'y ajoute la géométrie : J est à **3,797 m** de I, quand toutes les autres travées font **7,144 à 7,700 m**.
Ce n'est pas une travée, c'est une demi-travée en rive. Une poutre qui admet le cinquième de ses voisines, un
point sur deux, et qu'aucune étude ne charge, n'est pas une poutre sur laquelle on pose une ligne de mother.

Conséquences concrètes, inchangées :

- aucune ligne de mother ne s'appuie sur J ;
- la travée J–I, de 3,797 m, n'est jamais utilisée ;
- une charge située entre I et J part en porte-à-faux au-delà de I, et l'inventaire ne donne aucune valeur en
  porte-à-faux : la vérification n'est pas faite, elle devient une réserve de l'étude.

En coordonnées de la grille, I est à −29,306 m et J serait à −33,103 m de E5. Tout ce qui est à moins de
−29,306 − 0,08 m est traité comme un déport.

> **Le point ouvert, et il est réel : la dissymétrie avec le calcul de charge.** Le calcul, lui, connaît J,
> parce que `LoadTransmissionHelper.CharpenteSupportsOf` balaie **tout** `CharpenteCoordinates` et retient
> tout point à moins de 0,5 m de la ligne, jusqu'à 0,5 m au-delà de ses bouts. Une ligne de mother qui
> déborde au-delà de I peut donc se voir attribuer une charge sur un point J que l'optimiseur n'a jamais
> prévu — il l'avait dimensionnée en porte-à-faux, sans appui de ce côté-là. Le § 8.4 donne le seuil :
> dès que le débord atteint 3,297 m. Justifier l'absence de J du côté de l'implantation ne la justifie pas
> du côté du calcul : les deux ne parlent alors plus de la même poutre, et rien dans le code ne les
> réconcilie.

#### Il suppose que le point 0 de l'événement est E5

`Geometry` construit sa grille à partir de `CharpenteCoordinates` seul : les abscisses des poutres sont prises
relativement à E5, les ordonnées des lignes de points sont les Y bruts, et E5 y vaut (33,103 ; 0) en mètres,
donc Y = 0. `Pipeline.GenerateLayout` passe les X et Y stockés des charges **tels quels**. Il ne lit jamais
`Event.Point0X` ni `Event.Point0Y`. Le calcul charpente et l'étude, eux, les lisent. Sur un événement dont le
point 0 ne serait pas E5, l'implantation serait placée ailleurs que le calcul ne la relit.

#### Il ne connaît ni hauteur, ni moteur, ni flèche

`Palonnier.Level` vaut 1 et n'est affecté nulle part. Aucune altitude n'est calculée ni stockée. Deux lignes
de mother empilées dans la même travée sont indiscernables sur le plan : `Pipeline.BuildElementEntities` le dit
explicitement, et c'est pourquoi la correspondance entre une `Mother` et sa ligne en base se fait par identité
d'objet et non par géométrie. Ne sont modélisés nulle part : les moteurs et leur capacité, les élingues et les
brides, la flèche, la stabilité latérale, le renversement, les effets dynamiques, la température, les
interférences entre ponts.

#### Ce qui reste dans le code sans être utilisé

La liste a fondu. `Geometry.EnclosingCols`, les espacements moyens `Geometry.DX` et `Geometry.DY`, et les
deux dictionnaires `Geometry.ColX` et `Geometry.RowY` ont été **supprimés** : personne ne les appelait, et les
trois commentaires de `PalonnierHanging.cs` qui citaient encore `EnclosingCols` comme la règle en vigueur
nomment maintenant `MotherLines.Plan` et le débord porté par `Mother.XL` et `Mother.XR`. Ce qui subsiste :

| Élément | État réel |
|---|---|
| `Geometry.Grid` | la grille complète (poutre, ligne) → (x, y), construite au démarrage et lue nulle part |
| `Palonnier.XFix`, `Mother.Fits` | propriétés publiques sans appelant |

#### Les falaises de calcul

`GroupGenerator` énumère, pour chaque paquet de charges de même x, **tous** les sous-ensembles : 2^n − 1
groupes pour n charges au même x, sans plafond. Les groupes inclinés, eux, sont plafonnés à 4 charges
(`maxSize = Math.Min(4, n)`) mais sont pris sur **toutes** les charges : C(n,2) + C(n,3) + C(n,4), soit environ
4,1 millions de combinaisons pour 100 charges. Un plan très fourni sur un même axe, ou très nombreux, fait
donc exploser le temps de génération. Aucune protection, aucun délai maximal, aucun message.

Attention à ne pas se rassurer avec le budget du chapitre 16 : il borne la **recherche exacte**, qui vient
après, et pas l'énumération des groupes candidats, qui a lieu dans les deux modes et n'a toujours pas de
garde-fou.

*Source : `Shared/ScenarioHelpers/Core/Optimizer.cs`, `SelectGreedy`, `PreferGroup` et `Optimize` ;
`Shared/ScenarioHelpers/Core/Objective.cs` ; `Shared/ScenarioHelpers/Core/GroupGenerator.cs`, `Generate` ;
`Shared/ScenarioHelpers/Core/Geometry.cs`, l'en-tête sur la poutre J ;
`Shared/LoadTransmissionHelper.cs`, `CharpenteSupportsOf`.*

### 12.2 Les pièges de lecture d'un résultat

#### Piège 1 — « charge au point d'accroche » ne veut pas dire la même chose partout

C'est le piège le plus coûteux, parce que les deux nombres sont justes et se contredisent.

- Sur le plan PNG et dans la feuille *Points grille*, la charge d'un point est **scénique seule**.
  `Mother.Compute` n'additionne que `p.F`, la force d'origine scénique ; la part de poids propre `p.W` est
  ignorée. Le pied de page du plan le répète.
- Dans la feuille *Travées*, la charge d'une travée **comprend** le poids propre des palonniers accrochés :
  `BayLoadsOf` additionne `p.F + p.W`.
- Dans l'étude Word, la charge d'un point d'accroche comprend le poids propre des ponts, parce que le calcul
  charpente transmet les masses. C'est l'identité que l'étude vérifie :
  `somme(points) = somme(charges posées) + somme(masses cataloguées)`.

Le plan donne donc systématiquement **moins** que l'étude au même point. Ce n'est pas une erreur : ce sont
deux conventions.

> Le poids propre d'une ligne de mother n'est compté nulle part dans la charge de ses propres travées. La
> charge ponctuelle au milieu de l'inventaire est une charge utile, hors poids propre du pont ; le comparer à
> une charge incluant le poids du pont serait faux dans l'autre sens.

#### Piège 2 — le pont dessiné n'est pas le pont monté

Un élément en base est un **segment** : (X1, Y1) → (X2, Y2). Pour un palonnier, c'est sa portée A–B ; pour une
ligne de mother, ses extrémités dessinées XL et XR, porte-à-faux compris. L'assemblage, lui, doit seulement
atteindre `portée − 0,08 m` ; il est presque toujours plus long.

Exemple entièrement vérifiable sur les fichiers d'exemple livrés
(`AliantWebS/wwwroot/exemples/MotherGrill.xlsx`). Une ligne de mother de la ligne de points 3 va de la poutre
F à la poutre D : XL = −7,156 m, XR = +7,144 m, longueur dessinée **14,300 m**. L'assemblage doit atteindre
14,300 − 0,08 = 14,220 m. Les deux modèles en stock ne l'atteignent qu'à trois pièces de 5,6 m, soit
**16,80 m** ; à nombre de pièces et longueur égaux, c'est le plus léger qui gagne — C500F / TABMA, 3 × 47,8 =
143,4 kg, contre 3 × 95,6 = 286,8 kg pour S500 Carrée Acier. **2,50 m de pont ne figurent donc sur aucun
dessin**, et personne ne sait de quel côté ils dépassent.

Le classeur Excel distingue les deux : la colonne *Longueur assemblée (m)* donne 16,80 et la colonne
*Longueur dessinée (m)* donne 14,30. Le plan PNG, lui, ne dessine que la seconde, avec des extrémités
franches (`StrokeCap.Butt`) : ce qui est tracé est exactement la longueur dessinée.

#### Piège 3 — la position des jonctions n'est enregistrée nulle part

`MotherGridPalonnier.Composition` stocke `"[560,560,560]"` : des longueurs de pièces, en centimètres entiers,
la plus longue d'abord. Ni l'ordre réel sur le pont, ni la position des jonctions. Le commentaire d'en-tête de
`Assembly.cs` le dit sans détour : *« Where the joints fall does not matter. »* Aucun calcul du logiciel ne
dépend de leur position — et c'est précisément l'hypothèse que l'étude Word demande à l'ingénieur signataire
d'accepter lorsqu'une valeur est calculée : *« en admettant que les jonctions entre les pièces d'un pont
assemblé transmettent la totalité du moment »*.

#### Piège 4 — la valeur admissible est lue à la portée, jamais à la longueur du pont

Toujours sur l'exemple : la ligne de mother des lignes de points 2 et 8, qui ne couvre que la travée E–D
(7,144 m), est assemblée en 5,6 + 2,8 = **8,40 m** de S500 Carrée Acier. Sa valeur admissible n'est pas celle
d'un pont de 8,40 m : `LoadTable.At` est interrogée avec la **portée de la travée**, 7,144 m. Aucune ligne du
modèle n'atteint 7,144 − 0,01 = 7,134 m (la plus longue fait 7,0 m), donc la valeur est calculée à moment
égal :

```
l0 = 7,00 m     P0 = 2 000 kg     poids propre 96 kg  ->  p = 96/7 = 13,714 kg/m
M  = 2000 × 7 / 4 + 13,714 × 7² / 8 = 3 500 + 84,0 = 3 584 kg·m
P(7,144) = 4 × (3 584 − 13,714 × 7,144² / 8) / 7,144
         = 4 × (3 584 − 87,49) / 7,144 = 13 986 / 7,144 = 1 958 kg
```

La colonne *Source de la valeur* du classeur écrira alors « calculée à moment égal depuis la longueur de 7 m
(2 000 kg, poids propre 13,71 kg/m) ».

#### Piège 5 — un palonnier ne pend pas de ses deux extrémités

`BuildPalonnier` calcule RA et RB par bras de levier sur deux appuis, puis `HangOnCrossings` **écrase** tout :
le palonnier est accroché à chaque ligne de mother qu'il croise, chaque charge va aux deux accroches qui
l'encadrent, et RA / RB ne sont plus que les réactions aux deux lignes d'extrémité. Si le palonnier a des
accroches intermédiaires, **RA + RB est inférieur à sa charge totale**. Le reste est dans la feuille
*Accrochages*, pas dans les colonnes RA / RB.

#### Piège 6 — la portée d'un palonnier incliné n'est pas l'écart entre ses charges

Un groupe incliné est ramené aux deux lignes de points qui l'encadrent : le code calcule l'intersection de la
droite des charges avec la ligne du dessous, puis avec celle du dessus. Deux charges distantes de 2,00 m en X
et de 1,00 m en Y, entre les lignes 5 (y = 0) et 4 (y = 3,700), donnent :

```
direction unitaire (0,8944 ; 0,4472)
A = (−2,000 ; 0)        B = (5,400 ; 3,700)
portée = √(7,400² + 3,700²) = √68,45 = 8,273 m
```

**8,27 m de barre pour 2,24 m entre les deux charges.** Avec le `Palonnier.xlsx` d'exemple, aucun modèle n'a
de valeur au-delà de 6,00 m (2 × la plus grande longueur, 3,0 m) : le groupe est déclaré infaisable et n'est
même pas créé. C'est pourquoi, sur cette salle et avec cet inventaire, les palonniers inclinés sont rares.

#### Piège 7 — un palonnier va toujours d'une ligne de points à la suivante

`EnclosingRows` ramène toujours les extrémités sur des lignes de points. Une charge isolée à 0,50 m d'une
ligne donne un palonnier de la ligne du dessous à celle du dessus — 3,700 m — pas une barre de 1 m. Et une
charge **hors** de la plage des lignes de points (|y| > 15,15 m) fait partir la fonction en repli : elle prend
les deux dernières lignes et la charge se retrouve en porte-à-faux au-delà d'une extrémité, avec une réaction
négative — un soulèvement — à l'appui opposé.

#### Piège 8 — le regroupement en x dépend de l'ordre de lecture

Les charges sont rangées dans des paquets de même x avec une tolérance de 0,08 m, **par rapport à la première
charge du paquet**, pas par rapport à une moyenne, et sans transitivité. Trois charges à x = 0,00 / 0,07 /
0,14 donnent deux paquets : {0,00 ; 0,07} et {0,14}. Le palonnier est posé à x = 0,00, la charge à 0,07 est
projetée dessus, et ses 7 cm de décalage transversal disparaissent du stockage. Ce décalage reste absorbé par la tolérance en travers d'un palonnier ; celle-ci vaut 0,21 m parce qu'elle est calée sur `Geometry.ColinearTol` (0,20 m) plus 1 cm (§ 13.3), et non sur les 8 cm du regroupement en x.

`Pipeline.GenerateLayout` fige donc l'ordre du plan — `Numero` d'abord, puis le nom trié comme une personne
numérote (T2 avant T10) — et le commentaire dit pourquoi : *« the same plan read in another order is placed
differently from one run to the next »*.

#### Piège 9 — M7 dans le classeur n'est pas « #7 » en base

Le classeur nomme une ligne `M{Id}` et un palonnier `P{Id}`, où l'`Id` est un compteur remis à zéro à chaque
exécution. Le nom en base, lui, est `{modèle} L={longueur}m #{indice+1}`, où l'indice est la position dans la
liste finale. Or l'optimiseur **retire** des lignes et des palonniers en cours de route (une ligne
inconstructible, un palonnier sans ligne à une extrémité, une ligne devenue vide). Après retrait, les
indices se décalent mais les Id ne sont pas renumérotés : **M7 peut être « … #5 » en base**. Le classeur
imprime les deux, côte à côte, dans les colonnes *Ligne* et *Élément en base* : c'est la table de
correspondance.

#### Piège 10 — l'équilibre est un total, pas une preuve

`BalanceOk` compare la somme des points d'accroche à la somme des charges portées, à 2,0 kg près. Le total est
invariant à l'endroit où la charge est tombée : il détecte une fuite, jamais un mauvais placement.

#### Piège 11 — un échec tardif laisse l'implantation remplacée

L'ordre du pipeline est : vérifications, optimisation, refus éventuels, **enregistrement en base**, puis plan
PNG, puis classeur Excel. Tous les refus disent « L'implantation existante n'a pas été modifiée ». Mais si le
dessin ou l'export échoue, la page affiche quand même « Génération impossible : … » alors que **l'implantation
a déjà été remplacée**. Le message est alors trompeur ; le plan et le classeur du dossier de sortie sont, eux,
ceux de l'exécution précédente.

*Source : `Shared/ScenarioHelpers/Models/Models.cs`, `Mother.Compute` et `Mother.BayLoadsOf` ;
`Shared/ScenarioHelpers/Core/Optimizer.cs`, `HangOnCrossings` ;
`Shared/ScenarioHelpers/Core/GroupGenerator.cs`, `GenerateYGroups` et `GenerateBiaisGroups` ;
`Shared/ScenarioHelpers/Pipeline.cs`, `GenerateLayout` ; `Shared/ScenarioHelpers/LayoutArtifacts.cs`, `Write`.*

### 12.3 Les idées reçues

#### « L'optimiseur trouve la meilleure implantation »

Non, et il faut être précis sur ce « non », parce qu'il a changé de sens.

Ce qui est vrai depuis les chapitres 15 et 16 : **on sait maintenant ce que « meilleure » veut dire.** Quatre
contraintes dures, puis cinq critères dans un ordre écrit — moins de palonniers, moins de masse suspendue,
moins de pièces, moins de longueur, plus de marge — et chaque implantation mesurée avec la même règle.

Ce qui reste faux : au réglage par défaut (`bounds`), l'optimiseur ne cherche pas la meilleure au sens de ce
classement. Il est glouton, il prend d'abord le groupe qui porte le plus de charges, il sort les pièces du
stock au fur et à mesure et il ne revient jamais en arrière. Une pièce n'est rendue que dans deux cas : une
ligne de mother qu'aucun modèle ne peut construire, et un palonnier qui n'a pas de ligne à l'une de ses
extrémités. Aucun autre retour arrière n'existe.

Au réglage `exact`, il la cherche — et il dit quand il l'a prouvée. La phrase à retenir est alors plus étroite
que « la meilleure implantation » : *parmi toutes les façons de couvrir ces charges avec ces groupes
candidats, aucune ne donne une implantation que ce classement préfère.* Une autre suite de lignes de mother,
un autre choix de pièces, un groupe qu'aucun générateur n'a proposé, sont hors de ce qui a été parcouru
(§ 16.5).

Il ne minimise pas davantage le gâchis de stock. `Assembler.Cheavest` — pardon, `Assembler.Cheapest` — choisit
le moins de pièces, puis le moins de longueur en trop, puis le plus léger. Sur l'inventaire d'exemple, un
palonnier de la plus petite travée (3,600 m) doit atteindre 3,520 m : une seule pièce n'y suffit jamais, deux
sont toujours nécessaires. Le modèle F34200 compte 9 pièces de 2,00 m : elles font 4 palonniers, et **la
neuvième pièce ne pourra jamais servir**. 2,00 m de pont sur 18,00 m sont perdus par construction. L'optimiseur
ne le sait pas et ne le dit pas.

#### « Un pont dessiné sur 14,30 m est un pont de 14,30 m »

Non : c'est la portée qu'il franchit. Le pont monté est celui du nom de l'élément et de sa composition —
16,80 m dans l'exemple du § 12.2. C'est ce que l'équipe descend du camion, ce que l'inventaire pèse, et ce que
le tableau des ponts de l'étude doit imprimer. `ReportDataBuilder.ReferenceLengthOf` lit d'ailleurs la longueur
**dans le nom** de l'élément, précisément parce que le segment ne la donne pas.

#### « On sait où sont les jonctions »

Non. Voir le § 12.2, piège 3.

#### « Une charge admissible calculée vaut une charge d'abaque »

Non. Au-delà de la plus grande longueur que l'inventaire donne pour un modèle, la valeur n'est pas mesurée :
elle est **déduite** de la plus longue ligne à moment égal, en supposant la jonction parfaite et en ignorant
la flèche. Le code la borne à 2 fois cette longueur (`LoadTable.ExtrapolationLimit = 2.0`) et la refuse si le
poids propre de la ligne n'est pas renseigné. Le commentaire d'en-tête de `LoadTable.cs` précise le domaine de
validité constaté : *« within 6 % on the safe side up to three times the base length and is 22 % over at three
and a half »* — c'est-à-dire qu'au-delà de 3 fois, la formule devient dangereuse, d'où la borne à 2. Quatre messages de refus existent :

```
« MODÈLE » ne figure pas dans l'inventaire
portée de plus de 2 fois la plus grande longueur de l'inventaire (7 m)
poids propre inconnu pour la longueur de 7 m, dont la valeur serait calculée
le poids propre seul atteint le moment admissible
```

Chacun devient une vérification non effectuée dans l'étude — jamais un dépassement, jamais un succès.

#### « Si l'étude n'est pas d'accord avec l'optimiseur, l'un des deux a un bug »

Non, et c'est le point le plus important du chapitre. **L'optimiseur dimensionne avec la lecture même que
l'étude contrôle** : une seule classe, `RiggingOptimizer.IO.LoadTable`, sert les deux. `Optimizer.AssemblyFor`
et `MotherLines.Size` l'interrogent au placement ; `ReportDataBuilder.BuildAbaqueChecks` et
`BuildMotherAbaqueChecks` l'interrogent à l'édition, sur `Inventory.PalonnierTable` et `Inventory.MotherTable`.
De même, la règle d'accrochage est lue une seule fois, dans `Helpers.PalonnierHanging.Resolve`, et le partage
des charges entre appuis suit la même règle affine des deux côtés.

Donc, si l'étude signale un dépassement que l'optimiseur n'avait pas vu, **quelque chose a bougé entre les
deux** : une charge déplacée ou repesée sur la page Charges, un élément déplacé à la main, un accrochage
décroché à la main, une ligne de l'inventaire corrigée, ou simplement un calcul charpente qui n'a pas été
relancé. Toutes les réserves du § 13.3 servent à dire *laquelle*.

#### « Le réglage “orientations orthogonales uniquement” est cosmétique »

Non. Il supprime purement et simplement la génération des groupes inclinés
(`GroupGenerator.Generate(..., allowBiais: false)`). Des charges que seul un palonnier incliné pouvait
regrouper se retrouvent alors seules, chacune sur son propre palonnier, ou pas portées du tout — et le message
de refus le rappelle : *« Le réglage « orientations orthogonales uniquement » est activé : aucun palonnier
incliné n'a été proposé. »* C'est un réglage global de la page Réglages, clé `optimizer.orthogonalOnly`, par
défaut **désactivé**. Il est aussi le seul des trois réglages du chapitre 11 à toucher la géométrie : les
deux autres — le classement et le niveau de recherche — ne changent pas ce qu'un palonnier peut être, mais
laquelle des implantations possibles est retenue.

#### « Les 8 cm sont un arrondi d'affichage »

Non. `Geometry.TOL = 0.08` décide de choses irréversibles :

| Où | Ce que les 8 cm décident |
|---|---|
| `Optimize`, étape 1 | une charge à moins de 8 cm d'une ligne de points pend **directement** sur le mother, sans palonnier |
| `GenerateYGroups` | deux charges sont au « même x » à 8 cm près |
| `EnclosingRows` | une extrémité de palonnier est acceptée sur une ligne de points à 8 cm près |
| `MotherLines.Plan` | une demande à moins de 8 cm d'une poutre est **sur** la poutre et n'ouvre pas de travée |
| `AssemblyFor`, `MotherLines.Size` | l'assemblage n'a besoin d'atteindre que `portée − 8 cm` |
| `Optimizer.OnLine` | un point d'accroche jusqu'à 8 cm au-delà du bout dessiné d'une ligne est ramené sur ce bout |

Deux autres tolérances vivent à côté : `ColinearTol = 0,20 m` (l'écart maximal d'une charge à l'axe d'un
palonnier incliné) et `Mother.OnSupportM = 0,010 m` (à 1 cm d'une poutre, une charge est sur la poutre et ne
charge aucune travée).

#### « Une implantation partielle est enregistrée »

Non. Dès qu'une charge n'est portée ni par un palonnier ni par un mother, rien n'est écrit : ni en base, ni le
plan, ni le classeur. C'est aussi pourquoi certaines choses **ne s'affichent jamais** dans une sortie produite
par « Générer l'implantation » : la croix grise « charge non portée » de la légende du plan, la ligne
« Charges non portées » de la synthèse Excel autrement qu'à 0, et la mention « sans objet : aucune charge ».
Ces cas existent dans le code, ils sont inatteignables par ce chemin.

#### « Deux exécutions du même plan donnent deux implantations différentes »

Non, si rien n'a changé : tout est déterministe — l'ordre du plan est figé, les modèles sont parcourus par
ordre alphabétique, les accrochages sont traités par identifiant de palonnier puis par distance. En revanche,
**renuméroter ou renommer les charges change le résultat**, parce que l'ordre de lecture décide du
regroupement en x et de la première charge qui fixe la position du palonnier.

#### « Une utilisation inférieure à 100 % veut dire que c'est bon »

Non. Cette colonne compare une charge à une charge ponctuelle au milieu, lue ou calculée, pour **cette travée
là**. Elle ne dit rien de la capacité du point d'accroche, rien de la capacité totale de la poutre, rien du
porte-à-faux — que l'inventaire ne couvre pas — et rien d'un soulèvement. Ces quatre-là sont l'affaire de
l'étude.

*Source : `Shared/ScenarioHelpers/Core/Assembly.cs`, `Assembler.Cheapest` ;
`Shared/ScenarioHelpers/LoadTable.cs`, `LoadTable.At` ;
`Shared/ScenarioHelpers/Report/ReportDataBuilder.cs`, `ReferenceLengthOf` et `BuildAbaqueChecks`.*

## Chapitre 13 — Diagnostiquer une implantation

### 13.1 Lire le message quand rien n'est placé

Tous les refus remontent au même endroit : la page attrape l'exception et l'affiche telle quelle, en une seule
alerte, préfixée de « Génération impossible : ». Le texte du message est donc la totalité du diagnostic. Il existe six familles. Les deux qui portent sur les fichiers d'inventaire (§ 5 et § 6) surviennent les premières, avant même la lecture du plan : `GenerateLayout` charge l'inventaire à son étape 1. Viennent ensuite le plan vide, le plan inexploitable, les charges non portées, puis le déséquilibre.

#### 1. Le plan est vide

```
Le scénario n'a aucune charge : la page Charges est vide. Ajoutez-y les charges, ou importez-les
avec le bouton « Import Excel », puis relancez la génération. L'implantation existante n'a pas été
modifiée.
```

Rappel utile : **le plan, ce sont les lignes de la page Charges**, jamais `Charges.xlsx`, que le placement
n'ouvre pas.

#### 2. Le plan n'est pas exploitable

Toutes les anomalies sont réunies en un seul message, au plus dix nommées, puis « (+k) » :

```
Le plan de charges de la page Charges ne peut pas être placé : la charge en X = 3,2 m, Y = ? m (154 kg) : sans nom ; … . L'implantation existante n'a pas été modifiée.
L'implantation existante n'a pas été modifiée.
```

Les cinq anomalies possibles, et une non-anomalie :

| Anomalie | Texte |
|---|---|
| nom vide | `{étiquette} : sans nom` |
| nom en double | `« T4 » : deux charges portent ce nom` |
| X ou Y manquant | `« T4 » : position incomplète (X = 3,2 m, Y = ? m)` |
| poids manquant | `« T4 » : charge manquante` |
| poids négatif | `« T4 » : charge négative (−50 kg)` |
| poids de 0 kg | **accepté** : un point de charge qui ne pèse rien doit quand même être atteint |

Une charge sans nom ne peut pas être désignée par un numéro : elle est décrite par ce qu'elle a, sa position
et son poids, dans les axes des études — `la charge en X = 3,2 m, Y = ? m (154 kg)` — et un « ? » n'est jamais
remplacé par un 0, qui la placerait sur E5 sans le dire.

#### 3. Des charges ne sont portées par rien

C'est le message le plus riche, produit par `Pipeline.UncoveredMessage`. Il s'assemble ainsi :

```
{n} charge(s) sur {total} ne sont portées ni par un palonnier ni par un mother : {8 noms max, puis (+k)}.
[ Palonnier.xlsx n'a aucune pièce en stock. ]
[ Même assemblé de toutes ses pièces, le plus long palonnier de Palonnier.xlsx mesure {L} m,
  alors que deux lignes de points sont espacées d'au moins {g} m. ]
[ MotherGrill.xlsx n'a aucune pièce en stock. ]
[ Même assemblé de toutes ses pièces, le plus long mother de MotherGrill.xlsx mesure {L} m,
  alors que les poutres sont espacées de {min} à {max} m. ]
[ jusqu'à 3 messages « Ligne de mother … » ]
[ Le réglage « orientations orthogonales uniquement » est activé : aucun palonnier incliné n'a été proposé. ]
L'implantation existante n'a pas été modifiée.
```

Les espacements cités sont ceux de la salle : **de 3,600 m à 4,101 m** entre lignes de points voisines, **de
7,144 m à 7,700 m** entre poutres voisines (poutre J exclue, § 12.1). « Le plus long » est calculé en mettant
bout à bout toutes les pièces d'un même modèle, au plus 16 (`Assembler.MaxPieces`), et en gardant le meilleur
modèle. Sur l'inventaire d'exemple : 48,0 m côté palonniers (16 × 3,0 m de S500 Carrée Alu) et 89,6 m côté
mothers (16 × 5,6 m de S500 Carrée Acier). Ces deux phrases ne se déclenchent donc quasiment jamais.

> Ces deux phrases parlent de **longueur seulement**. Leur absence ne veut pas dire que le stock est assez
> fort : elle veut dire qu'il est assez long. La cause la plus fréquente, une charge trop lourde pour tout
> modèle sur cette portée, ne produit aucune phrase dédiée.

Les messages « Ligne de mother » viennent de `MotherLines.Explain` et sont, eux, très précis :

```
Ligne de mother F3–D3 : aucune pièce de MotherGrill.xlsx n'est en stock.
Ligne de mother F3–D3 : les pièces restant en stock ne permettent pas d'assembler 14,22 m
                        d'un modèle qui la porte.
Ligne de mother F3–D3 : 4 900 kg sur la travée E3–D3 (7,14 m), alors que le modèle le plus fort
                        en stock y admet 4 244 kg.
```

Le deuxième signifie qu'un modèle *porterait* la ligne mais que ses pièces sont déjà prises ailleurs : c'est
le glouton du § 12.1 qui se voit. Le troisième donne la travée fautive, sa portée et la meilleure valeur
disponible : de quoi décider s'il faut alléger, déplacer ou acheter.

#### 4. L'implantation ne s'équilibre pas

```
Les points d'accroche portent 4 210 kg pour 4 250 kg de charges : l'implantation calculée n'est
pas cohérente et n'a pas été enregistrée. L'implantation existante n'a pas été modifiée.
```

Le seuil est de 2,0 kg. La cause typique est nommée dans le code : deux charges portant le même nom, que
l'optimiseur distingue par le nom seul. Comme le plan est déjà refusé pour cela en amont, ce message ne
devrait plus se produire ; s'il se produit, c'est un défaut du moteur et non des données.

#### 5. Un fichier d'inventaire manque

```
Fichier d'entrée manquant : MotherGrill.xlsx. Ajoutez-le aux fichiers du scénario, ou au dossier
partagé _common/data.
```

Seul l'inventaire est lu sur disque : `MotherGrill.xlsx` et `Palonnier.xlsx`, la copie du scénario d'abord,
puis `_common/data`.

#### 6. Deux fichiers ne diffèrent que par la casse

```
Le dossier « … » contient plusieurs fichiers « MotherGrill.xlsx » dont les noms ne diffèrent que
par les majuscules : MotherGrill.xlsx, mothergrill.xlsx. Gardez-en un seul.
```

Windows ne fait pas la différence, l'image Docker tourne sous Linux qui la fait. Le fichier est cherché sans
tenir compte de la casse, et deux candidats sont refusés plutôt que l'un choisi au hasard.

#### Ce que le message ne dit pas

- **Aucun fichier n'est produit en cas de refus.** Le plan PNG et le classeur ne sont écrits qu'après
  l'enregistrement. Le dossier de sortie contient donc encore les fichiers de l'exécution précédente : ne les
  lisez pas comme un diagnostic de celle qui vient d'échouer. Vérifiez leur date.
- **Un palonnier retiré faute de ligne de mother à une extrémité n'apparaît pas.** L'erreur
  `{palonnier} : aucune ligne de mother à l'une de ses extrémités.` ne commence pas par « Ligne de mother » :
  elle n'est pas reprise dans le message. Ses charges, elles, deviennent non portées et sont nommées — sans
  leur raison.
- **Au plus trois causes de lignes** sont citées, et seulement si le stock de mothers n'est pas vide.
- **Les dépassements de valeur admissible constatés ne sont pas dans le message.** Ils vivent dans
  `RiggingResult.Errors`, et ne sont visibles que dans la feuille *Avertissements* d'une exécution qui, elle,
  a réussi.

*Source : `Shared/ScenarioHelpers/Pipeline.cs`, `GenerateLayout` ; `Shared/ScenarioHelpers/PlanSource.cs`,
`Read` ; `Shared/ScenarioHelpers/Pipeline.Messages.cs`, `UncoveredMessage` ;
`Shared/ScenarioHelpers/Core/MotherLines.cs`, `Explain` ; `Shared/ScenarioHelpers/InputFiles.cs`, `Find`.*

### 13.2 Les sorties : le plan PNG, le classeur Excel, l'étude Word

Une exécution réussie écrit deux fichiers dans le dossier de sortie du scénario :
`rigging_plan_v5.png` et `rigging_report_v5.xlsx`. L'étude Word est produite séparément, par le bouton de
rapport, et lit la **base**, pas ces deux fichiers.

#### Le plan PNG

Il est dessiné comme la Figure 2 des études manuscrites : poutres horizontales, **J en haut et A en bas**,
lignes de points 1 à 9 de gauche à droite, dans les axes des études (origine E5, X le long des poutres vers le
point 9, Y en travers vers J). Une seule échelle pour les deux axes — un plan étiré se lirait comme un autre
rig. L'échelle vaut au plus 34 px/m et l'image au plus 3 000 px de côté utile ; une coordonnée aberrante
rétrécit donc tout le dessin.

| Ce qui est dessiné | Comment |
|---|---|
| poutres | traits gris horizontaux, lettre en pastille ambre des deux côtés |
| lignes de points | traits vert pâle verticaux, numéro en pastille rouge en haut et en bas |
| points d'accroche | pastilles vertes |
| ligne de mother | segment violet vertical, de XL à XR, extrémités franches ; un triangle pointe en haut à chaque poutre sur laquelle elle s'appuie ; étiquette `M{Id}` |
| palonnier le long des poutres | segment vert-bleu ; incliné : segment orange ; étiquette `P{Id}` |
| accroche d'un palonnier | triangle pointe en bas, à chaque ligne à laquelle il pend |
| charge sur palonnier | losange jaune, avec un trait pointillé jusqu'à son point d'appui sur la barre |
| charge sur une ligne de mother | disque rose cerclé de blanc |
| point chargé | anneau rouge, rayon croissant avec la charge, étiquette en kg ; **bleu et « soulèvement » si la charge est négative** |

Le cartouche donne les comptes, le poids total, le total aux points d'accroche, l'état de l'équilibre, le
réglage orthogonal s'il est actif, et le rappel des axes. Le pied de page rappelle que les charges aux points
sont **scéniques seules**, et indique le nombre d'étiquettes qui n'ont pas trouvé de place : celles-là ne sont
pas perdues, leurs valeurs sont dans le classeur.

Deux pièges déjà signalés : le plan dessine la poutre J alors que l'optimiseur ne s'en sert jamais (§ 12.1),
et il dessine la longueur **franchie** par un pont, jamais sa longueur assemblée. Le coût mesuré, le plancher
et le classement appliqué ne sont **pas** sur le plan : ils sont sur la console et dans la feuille Synthèse du
classeur.

#### Le classeur Excel : neuf feuilles

Les huit feuilles de détail sont des tables : une légende en ligne 1 qui dit ce qu'elles contiennent, les en-têtes en ligne 2, les données à partir de la ligne 3. La Synthèse, elle, est une liste d'intitulés et de valeurs : titre en ligne 1, légendes en lignes 2 et 3, valeurs à partir de la ligne 5. Les nombres sont écrits **non arrondis** et arrondis une seule fois par le
format de la cellule (`#,##0` pour les kg, `0.000` pour les mètres, `0.0%` pour les taux) ; une phrase et une
cellule ne se contredisent donc jamais.

| Feuille | Ce qu'elle contient | À quoi elle sert |
|---|---|---|
| Synthèse | comptes, poids total, charges portées, total aux points, équilibre, pièces utilisées, nombre d'avertissements ; puis le **coût mesuré** (les neuf quantités de `LayoutCost`, § 15.4) et le **plancher théorique** avec l'écart au plancher (§ 16.2) | première lecture : l'implantation tient-elle, y a-t-il des réserves, et de combien pourrait-elle, au plus, être au-dessus du minimum |
| Palonniers | un palonnier par ligne : modèle, pièces, longueur assemblée, portée A–B, charges, charge totale, admissible sur A–B, source de la valeur, utilisation, poids propre, XA/YA/RA, XB/YB/RB, lignes en A et B, lignes accrochées, lignes croisées non accrochées, état | vérifier le dimensionnement d'une barre et retrouver son élément en base |
| Lignes de mother | une ligne par mother : modèle, pièces, longueur assemblée, ligne de points, X, première et dernière poutre, Y1/Y2, longueur dessinée, « charge / admissible » travée par travée, utilisation max, travée la plus sollicitée, porte-à-faux, charges directes, palonniers accrochés, charge portée, charges scéniques aux poutres, poids propre, état | voir d'un coup quelle ligne est en limite et par quelle travée |
| Travées | chaque travée de chaque ligne : portée, charge, admissible, source de la valeur, utilisation, état ; puis une ligne par porte-à-faux | le détail du calcul ; c'est ici que se lit une valeur « calculée » |
| Charges sur mother | les charges à moins de 8 cm d'une ligne de points : poids, ligne, écart à la ligne, position sur la ligne depuis Y1, emplacement, report à chaque poutre par bras de levier | contrôler les charges qui ne passent par aucun palonnier |
| Accrochages | pour chaque palonnier : chaque accroche (A, croisement, B) avec X, Y, distance depuis A, réaction, poids propre repris, ligne concernée, emplacement sur la ligne ; puis chaque croisement **non** accroché avec sa raison | comprendre où part la charge, et pourquoi un croisement a été laissé |
| Points grille | la charge scénique de chaque point d'accroche, la plus forte d'abord, et l'apport de chaque ligne ; totaux et écart en bas | comparer au cahier des charges de la salle, en sachant que le poids propre n'y est pas |
| Stock barres | les lignes d'inventaire de Nombre > 0 : nombre, utilisées, restantes, charge ponctuelle au milieu, poids propre, « épuisé » ; totaux par fichier | voir ce qui a été consommé et ce qui bloquera la prochaine génération |
| Avertissements | d'abord ce que l'optimiseur signale, puis ce que l'export relève : travée chargée sans valeur admise, porte-à-faux, poids propre inconnu, soulèvement | la liste des vérifications qui restent à faire à la main |

Deux colonnes méritent d'être lues ensemble partout : *Portée* (ou *Longueur dessinée*) et *Longueur
assemblée*. Leur écart est le pont qui dépasse.

#### L'étude Word

Nom du fichier : `(ddMM)ETUDE_IMPLANTATION_ANTARES_{SPECTACLE}.docx`, sans accents, en majuscules, comme les
études existantes. Structure :

1. **PRELIMINAIRES** — objet de l'étude, documents de référence.
2. **PRESENTATION DE LA SALLE** — système d'accroche, nomination des poutres et des points, cahier des charges.
3. **IMPLANTATION DANS LA SALLE** — plan de charges de la production (figure et Tableau 1), implantation
   (figure « Mother + pré rigg », Tableau 2 des ponts, liste des ponts assemblés de plusieurs pièces), figure
   de l'implantation du plan dans la salle, puis les résultats : les vérifications de ponts travée par travée (Mother, puis Pré rigg), et le Tableau des charges par point d'accroche.
4. **CONCLUSION** — trois issues, jamais deux, puis les *Réserves sur les données*.

Les trois issues sont explicites dans le code :

```
IsConclusive  = les charges aux points sont calculées ET aucune vérification ne manque
IsCompliant   = IsConclusive ET aucun dépassement démontré
```

Une étude non concluante dit : *« Cette étude n'est pas concluante : certaines vérifications n'ont pas pu être
effectuées faute de données … ne peut donc être déclaré ni conforme ni non conforme »*, puis énumère les
vérifications manquantes. C'est le point de conception à retenir : **une vérification non faite n'est pas une
vérification réussie**.

*Source : `Shared/ScenarioHelpers/PlanDrawer.cs`, `Draw` et `DrawTitle` ;
`Shared/ScenarioHelpers/ExcelExporter.cs`, `Export` ;
`Shared/ScenarioHelpers/Report/WordReportBuilder.cs`, `AppendConclusion` ;
`Shared/ScenarioHelpers/Report/ReportGenerator.cs`, `GenerateAsync`.*

### 13.3 Les réserves de l'étude, et ce qu'elles disent de l'implantation

L'étude tient deux listes distinctes, et il ne faut pas les confondre.

- `RigReport.Warnings` — les **réserves sur les données** : ce qui limite la portée du document sans empêcher
  de conclure (un plan de production non fourni, un inventaire absent, deux poids propres différents pour une
  même pièce, une charge sans coordonnées donc non dessinée).
- `RigReport.VerificationGaps` — les **vérifications non effectuées** : chacune bloque la conclusion.

La table ci-dessous classe les vérifications non effectuées selon ce qu'elles accusent.

| Réserve | Ce qu'elle accuse |
|---|---|
| aucune charge scénique enregistrée | le plan |
| charges sans poids | le plan |
| charges aux points d'accroche non calculées | le calcul charpente non lancé |
| capacité inconnue pour des points chargés, ou pour une poutre chargée | le cahier des charges de la salle (`TCharpente`) |
| ligne d'inventaire illisible pour un modèle utilisé | l'inventaire |
| masse de pièces inconnue | l'inventaire, et donc **toutes** les charges aux points, sous-estimées |
| pièces enregistrées illisibles, ou trop courtes pour la longueur dessinée | **l'implantation** |
| charge non vérifiée sur une portée sans valeur admissible | l'implantation, ou l'inventaire |
| déport (porte-à-faux) sans valeur admissible | **l'implantation** |
| soulèvement par un palonnier en porte-à-faux | **l'implantation** |
| réserves de transmission des charges | **l'implantation** |
| total imprimé ≠ total posé | le plan et l'implantation désynchronisés |
| total stocké ≠ charges posées + masse du pré rigg | le calcul charpente périmé, ou une fuite |
| tableau des points ≠ calcul actuel | le calcul charpente périmé, ou une saisie à la main |
| charges placées ailleurs que dans le plan | **l'implantation périmée** |

#### Les réserves de transmission

Elles viennent de `SelfWeightAccount.Describe` et décrivent toutes une implantation que le calcul n'a pas su
exploiter :

```
{n} élément(s) ne reposent sur aucun appui, leur poids propre n'a pas pu être transmis : …
{n} élément(s) n'ont qu'un seul appui, qui reçoit la totalité de leurs charges et de leur poids propre : …
{n} charge(s) scénique(s) reposent sur un palonnier qui n'est accroché à aucun mother : …
{n} charge(s) sur mother n'ont trouvé aucun point de charpente : … Vérifiez le point 0 de l'événement.
Les accrochages enregistrés de {n} palonnier(s) sont illisibles : ils ont été considérés comme accrochés
à tous les mothers qu'ils croisent : …
{n} extrémité(s) de palonnier sont accrochées au-delà de l'extrémité de leur mother, qui porte donc en
porte-à-faux : …
{n} mother(s) n'ont pas de point d'accroche sous l'une de leurs extrémités (écart indiqué) : …
```

La dernière, lorsqu'elle touche **tous** les mothers, désigne presque toujours un point 0 d'événement faux —
La dernière, lorsqu'elle touche tous les mothers, désigne presque toujours un point 0 d'événement faux — comme la réserve sur les charges de mother sans point de charpente, son texte propose lui-même la cause.

#### La vérification centrale : l'implantation porte-t-elle encore le plan ?

C'est `ReportDataBuilder.LayoutMismatches`. Elle existe parce que deux tables sont éditables séparément : le
plan (`ChargesScenique`, ce que l'étude **imprime**) et les charges posées (`ChargeMp`, ce que le calcul
**transmet**). Déplacer une charge sur la page Charges sans relancer le placement laisse les deux en désaccord,
et **toutes** les autres vérifications sont aveugles à cela, puisqu'elles travaillent toutes depuis
l'implantation stockée.

La méthode est celle-ci, et elle mérite d'être comprise :

1. Chaque charge posée est **remise à sa place** : on part de (X1, Y1) de son élément porteur et on avance de
   `ChargeMp.X` mètres vers (X2, Y2). C'est exactement la lecture que fait la transmission des charges
   (`LoadTransmissionHelper.PointOnElement`).
2. On la rapproche de la charge du plan **de même nom**, comparé sans casse après suppression des espaces.
3. On mesure l'écart en deux composantes : **le long** de l'élément et **en travers**.
4. Chacune est comparée à sa propre tolérance.

Les trois tolérances, et d'où elles viennent :

| Constante | Valeur | Origine |
|---|---|---|
| `PlacementToleranceM` | **0,15 m** le long de l'élément | l'optimiseur ne décale une charge le long de sa barre que dans deux cas : la projection sur l'axe d'un palonnier, et le recalage d'un point d'accroche sur le bout d'une ligne de mother, au plus 0,08 m (`Optimizer.OnLine`). Plus une marge |
| `AcrossPalonnierToleranceM` | **0,21 m** en travers d'un palonnier | `Geometry.ColinearTol` (0,20 m), l'écart maximal d'une charge à l'axe d'un palonnier incliné, plus 1 cm pour l'arrondi des extrémités |
| `AcrossMotherToleranceM` | **0,09 m** en travers d'une ligne de mother | `Geometry.TOL` (0,08 m), le seuil au-delà duquel une charge n'aurait pas été accrochée directement sur la ligne, plus 1 cm |

Exemple, à vérifier à la main. La charge L1 du plan d'exemple est en (2,99 ; 7,45) dans les axes de la grille ;
la ligne de points 3 est à y = 7,400, l'écart vaut 0,05 m ≤ 0,08 : elle pend directement sur la ligne de
mother F3–D3, dont XL = −7,156. On enregistre donc `ChargeMp.X = 2,99 − (−7,156) = 10,146 m`. L'étude refait
le chemin : depuis (−7,156 ; 7,400) vers (7,144 ; 7,400), 10,146 m plus loin, on retombe sur (2,990 ; 7,400).

```
écart le long    = |2,990 − 2,990| = 0,000 m   <= 0,15   ->  passe
écart en travers = |7,400 − 7,450| = 0,050 m   <= 0,09   ->  passe
```

Les 0,050 m ne sont pas une erreur : ce sont les 5 cm que l'optimiseur a absorbés en accrochant la charge sur
la ligne. La tolérance de 0,09 m est calibrée exactement pour eux. Si la charge avait été déplacée de 12 cm
sur la page Charges après le placement, l'écart en travers vaudrait 0,17 m et la réserve tomberait :

```
La charge « L1 » est placée en (-7,40 ; -2,99) sur « C500F / TABMA L=16,8m #2 » mais en
(-7,57 ; -2,99) dans le plan de charges : l'implantation ne correspond plus aux charges.
Relancez le placement (Générer l'implantation) puis le calcul charpente.
```

Sept familles d'écarts sont distinguées, chacune avec son propre message et sa propre consigne : au plus cinq
noms cités, puis « (+k) ».

| Famille | Cas |
|---|---|
| nom ambigu | charge sans nom, ou nom présent deux fois de l'un ou l'autre côté : rien ne peut être comparé |
| non placée | dans le plan, sur aucun élément |
| non planifiée | posée sur un élément, absente du plan |
| déplacée | au-delà d'une des deux tolérances ; les plus éloignées sont nommées d'abord |
| repesée | poids différents des deux côtés, au-delà de `max(1 kg ; 0,1 %)` |
| sans poids | aucun poids nulle part : comptée 0 daN, ce que l'étude ne peut pas vérifier |
| non localisable | élément introuvable, position manquante, coordonnées d'élément incomplètes, coordonnées de plan manquantes |

Les autres seuils de cohérence, pour mémoire :

| Contrôle | Seuil |
|---|---|
| total imprimé contre total posé | `max(1 kg ; 0,1 % du plus grand)` |
| total du tableau des points contre charges posées + masse du pré rigg | `max(1 kg ; 0,1 % de l'attendu)` |
| tableau stocké contre calcul refait, point par point | **1,0 kg** par point (`StaleCells`) |

#### Ce que ces réserves disent, au fond

Elles disent toujours la même chose sous des formes différentes : **l'implantation, le plan de charges,
l'inventaire et le tableau des points d'accroche sont quatre choses éditables séparément, et l'étude est le
seul endroit où elles sont confrontées.** Puisque l'optimiseur dimensionne avec la lecture que l'étude
contrôle (§ 12.3), une réserve n'accuse presque jamais le moteur : elle date les quatre objets les uns par
rapport aux autres. La consigne, répétée à l'identique dans la plupart des messages, en découle :
« Relancez le placement (Générer l'implantation) puis le calcul charpente. »

*Source : `Shared/ScenarioHelpers/Report/ReportDataBuilder.cs`, `BuildAsync`, `LayoutMismatches`, `Offsets`,
`PointOn`, `StaleCells`, `SkippedRowGaps`, `PieceProblemGaps` ; `Shared/SelfWeightAccount.cs`, `Describe`.*

## Chapitre 14 — Où c'est dans le code, et lexique

### 14.1 Carte des fichiers

Le moteur d'implantation vit sous `Shared/ScenarioHelpers/`. Il ne dépend pas de Blazor. Le dossier `Core/` ne touche ni la base, ni un fichier, ni une page : on peut y vérifier une règle sans rien avoir devant soi. Entity Framework n'apparaît que dans les fichiers d'orchestration — `Pipeline.cs`, `PlanSource.cs`, `LayoutPersistence.cs`, `Pipeline.Entities.cs`, `Pipeline.Imports.cs` — et la table des réglages dans `OptimizerSettings.cs` seul ; le dossier `Report/`, lui, reçoit `EntitiesService` et lit la base à son tour, pour l'étude Word.

#### Le cœur — `Shared/ScenarioHelpers/Core/`

| Fichier | Ce qui y vit |
|---|---|
| `Core/Geometry.cs` | la grille : les 9 poutres I..A et les 9 lignes de points, construites depuis `CharpenteCoordinates` relativement à E5 ; les tolérances `TOL` (0,08 m), `CrossingTol`, `ColinearTol` (0,20 m) ; `NearestRow`, `EnclosingRows`, `ProjOnAxis`, `Reactions2Pt`. L'en-tête du fichier porte la justification de l'absence de la poutre J, avec les chiffres des six études (§ 12.1) |
| `Core/Objective.cs` | **ce que « meilleur » veut dire** : les quatre `HardConstraints` avec le code qui les applique, le `LayoutCost` d'une implantation terminée, le `DefaultRanking` des cinq critères et leurs clés, `Parse` / `Format` du classement enregistré, `Compare`, `IsBetter`, `Decides`. Ne place rien et n'interdit rien : c'est l'étalon (chapitre 15) |
| `Core/Bounds.cs` | **le plancher** qu'aucune implantation d'un plan ne peut passer : `MotherMetresBound`, `PalonnierCountBound`, `PieceCountBound`, le `LayoutBound` qu'ils forment et le `LayoutGap` par rapport à ce qui a été produit (§ 16.2) |
| `Core/ExactSearch.cs` | **la recherche exacte** : `Run` parcourt toutes les couvertures des charges à suspendre par les groupes candidats, construit chacune par le même `Optimizer.Build` et garde la meilleure au sens du classement ; `StockMark`, le repère qui rembobine un stock entre deux branches (§ 16.3) |
| `Core/Assembly.cs` | un pont fait de plusieurs pièces d'un même modèle : `Assembly` (longueur, masse, composition), `PieceStock` (un stock qui ne prête jamais deux fois la même pièce : `Cheapest`, `Best`, `Take`, `Give`, `Snapshot`), `Assembler.Cheapest` (moins de pièces, moins de longueur en trop, plus léger) et `Assembler.MaxPieces` = 16 |
| `Core/GroupGenerator.cs` | tous les groupes de charges qu'un palonnier pourrait porter : `GenerateYGroups` (mêmes x à 0,08 m près, tous les sous-ensembles), `GenerateBiaisGroups` (2 à 4 charges alignées à 0,20 m près), `WithinAxis`, `Combinations` |
| `Core/MotherLines.cs` | où vont les lignes de mother et de quoi elles sont faites : `Plan` (une ligne par suite de travées chargées, sur une ligne de points), `LineAt`, `Size` (choix du modèle travée par travée), `Explain` (le message quand aucun modèle ne convient) |
| `Core/Optimizer.cs` | l'algorithme : `Optimize` (l'enchaînement, la mesure du coût et du plancher), `Build` (d'un choix de groupes à une implantation finie — étapes 3 à 7, la seule construction, partagée par le glouton et la recherche exacte), `LayoutWorkshop` (l'atelier : stocks, abaques, options), `SelectGreedy`, `PreferGroup` (la préférence entre deux candidats, sans nombre magique), `AssemblyFor`, `BuildPalonnier`, `HangAtEnds`, `HangOnCrossings`, `SupportReactions`, `Overload`, `OnLine`, `PointsOn` |

#### Les entrées–sorties — `Shared/ScenarioHelpers/`

| Fichier | Ce qui y vit |
|---|---|
| `Pipeline.cs` | **l'orchestration seule**, 131 lignes : `GenerateLayout` ouvre son propre contexte de base (`CreateOwnedContext`), appelle chaque étape et porte les deux refus qui comptent — charges non portées, déséquilibre |
| `PlanSource.cs` | le plan de charges tel que la page Charges le tient : `Read` (lecture `AsNoTracking`, ordre figé, tous les contrôles d'un coup, refus avant toute suppression) |
| `OptimizerSettings.cs` | les réglages devenus des options : `ReadAsync` (orthogonal, classement, niveau de recherche, et le repli annoncé quand une valeur est illisible), `Describe` |
| `LayoutPersistence.cs` | la base : `EnsureElementTypes`, `ElementTypesFor`, `ReplaceElementsAsync` (la transaction unique, dans la stratégie de reprise) |
| `LayoutArtifacts.cs` | `Write` : le plan PNG et le classeur, écrits après l'enregistrement — un échec ici coûte une image, jamais l'implantation |
| `Pipeline.Entities.cs` | d'une implantation finie aux lignes à écrire : `BuildElementEntities`, `BuildChargeMps`. Pur — aucune base, aucune horloge —, donc vérifiable en test |
| `Pipeline.Messages.cs` | ce que l'utilisateur lit quand le placement refuse : `UncoveredMessage`, `LongestAssembly`, `NamedRows` |
| `Pipeline.Imports.cs` | les trois `Import*FromStream` réutilisés par l'interface, et `MaxChargeNameLength` (50) |
| `Models/Models.cs` | les types du moteur : `Charge`, `BarSpec`, `Mother` (travées, `BayLoadsOf`, `Compute`, `Shares`, `OnSupportM` = 1 cm), `Palonnier`, `PalonnierSupport`, `SkippedCrossing`, `CandidateGroup`, `SearchLevel` (`bounds` / `exact`), `OptimizerOptions` (classement, niveau, budgets 2 000 ms et 20 000 nœuds), `SearchOutcome` (ce qui a réellement tourné), `RiggingResult` |
| `LoadTable.cs` | **la** lecture de la charge admissible, partagée par l'optimiseur et l'étude : `AllowedLoad`, `AllowedSource`, `LoadTable.From`, `LoadTable.At`, `LengthToleranceM` (0,01 m), `ExtrapolationLimit` (2,0) |
| `InventoryFiles.cs` | les deux classeurs d'inventaire : noms de fichiers, colonnes, `Load`, `ReadFile`, `ReconcileWeights`, `Inventory`, `SkippedRow` |
| `InputFiles.cs` | `Find` : trouver un fichier sans tenir compte de la casse, et refuser deux homonymes |
| `ExcelLoader.cs` | les feuilles remplies à la main : `LoadCharges`, `LoadMotherGridPalonniers`, `OpenShared` (lire un classeur ouvert dans Excel) |
| `ExcelCells.cs` | lire une cellule et reconnaître une ligne d'en-tête, partagé par les deux lecteurs : `Normalize`, `Headings`, `Text`, `Number`, `AllNumbers` |
| `ExcelExporter.cs` | le classeur de neuf feuilles : `Export`, une méthode par feuille, `LineCheck`, `BayCheck`, `Overhang`, `CollectWarnings` |
| `PlanDrawer.cs` | le plan PNG : `Draw`, `Venue.InStudyAxes`, `Layout`, `DrawVenue`, `DrawMothers`, `DrawPalonniers`, `DrawGridLoads`, `DrawCharges`, `LabelPlacer`, `DrawLegend` |

#### La persistance et les règles partagées — `Shared/`

| Fichier | Ce qui y vit |
|---|---|
| `Shared/StudyAxes.cs` | la conversion entre les axes de la grille et ceux des études, (x, y) → (−y, −x), sa propre inverse |
| `Shared/TrussComposition.cs` | les pièces d'un pont telles que la base les stocke : `Serialize`, `ParseText`, `Describe` |
| `Shared/PalonnierHanging.cs` | **la** lecture de la règle d'accrochage : `HookOrigin`, `HookRecord`, `PalonnierHooks` (le JSON de `Accrochages`), `Resolve` |
| `Shared/SelfWeightDistribution.cs` | le poids propre d'un pont réparti sur ses appuis, avec `CoincidentToleranceM` = 0,010 m |
| `Shared/SelfWeightAccount.cs` | ce qu'une transmission a su transmettre et ce qu'elle a perdu : `Describe` fournit les réserves du § 13.3 |
| `Shared/LoadTransmissionHelper.cs` | la transmission des charges jusqu'aux points d'accroche, utilisée par « Calcul Charpente » et rejouée par l'étude |
| `Shared/Data/Models/MotherGridPalonnier.cs` | un pont en base : `Famille` (vrai = mother), `X1/Y1/X2/Y2` (le segment franchi), `Name` (50 caractères, porte la longueur assemblée), `Composition`, `MotherAId`, `MotherBId`, `Accrochages` |
| `Shared/Data/Models/ChargeMp.cs` | une charge posée sur un pont : `Name`, `Numero`, `X` (distance le long du pont depuis (X1, Y1)), `Charge` |
| `Shared/Data/Models/ChargesScenique.cs` | une charge du plan : `Name`, `Numero`, `X`, `Y` (axes de la grille), `Charge` |
| `Shared/Data/Models/Charpente.cs` | les charges appliquées, point par point, pour un scénario |
| `Shared/Data/Models/Tcharpente.cs` | le cahier des charges de la salle, une seule ligne pour tout le lieu ; la ligne 10 porte la capacité totale de chaque poutre |
| `Shared/Data/Models/ElementType.cs` | le modèle d'un pont, créé au besoin dans la catégorie `mothergridpalonnier` |
| `Shared/Data/Models/CharpenteCoordinates.cs` | les coordonnées relevées des 90 points, en millimètres, exposées en mètres |
| `Shared/Data/AppSettings.cs` | les réglages globaux, dont les trois de l'implantation : `optimizer.orthogonalOnly`, `optimizer.objective.order` (les clés du classement, dans l'ordre) et `optimizer.searchLevel` (`bounds` ou `exact`) |

#### L'étude Word — `Shared/ScenarioHelpers/Report/`

| Fichier | Ce qui y vit |
|---|---|
| `Report/ReportDataBuilder.cs` | **tout ce qui est vérifié** : `BuildAsync`, `BuildBeamLoads`, `BuildTrussInventory`, `BuildAbaqueChecks`, `BuildMotherAbaqueChecks`, `LayoutMismatches`, `StaleCells`, `SkippedRowGaps`, `PieceProblemGaps`, `ReferenceLengthOf`, et les trois tolérances de recalage |
| `Report/ReportModel.cs` | les données pures du document : `RigReport`, `ProductionLoad`, `BeamLoadRow`, `AbaqueCheck`, `UnhookedCrossing`, `FigureSet`, et les verdicts `IsConclusive`, `HasExceedances`, `IsCompliant` |
| `Report/ReportGenerator.cs` | l'orchestration : `GenerateAsync`, `BuildFileName`, `BuildFigures`, la recherche du plan de production déposé |
| `Report/WordReportBuilder.cs` | le rendu OpenXml : en-têtes réels, champs PAGE / NUMPAGES, sommaire, tableaux, `AppendConclusion` |
| `Report/FigureScene.cs` | ce dont une figure est faite avant qu'on décide comment la dessiner : `FigureScene` et ses primitives en millimètres — trait, disque, cadre, polygone, polyligne, étiquette |
| `Report/ReportFigureDrawer.cs` | la **mise en page** des figures — cadrage, grille de la salle, treillis, étiquettes sans chevauchement, légende, échelle — écrite dans une `FigureScene`, puis rendue en PNG ; `BuildWeb` dessine l'âme croisée qui fait lire un treillis pour un treillis |
| `Report/FigureDrawingMl.cs` | la même scène rendue en **formes DrawingML** dans le document : nettes à tout zoom, texte sélectionnable, une fraction du poids d'une image |
| `Report/TrussCatalog.cs` | la masse, le stock et le nom imprimé de chaque pièce, depuis l'inventaire |
| `Report/NaturalStringComparer.cs` | le tri des noms comme une personne les numérote : T1, T2, … T10 |
| `Report/EmbeddedAssets.cs` | les images embarquées : logos, photo de la salle, figure de nomination des points |

#### Les pages qui déclenchent tout cela

| Fichier | Ce qui y vit |
|---|---|
| `AliantWebS/Pages/AntaresConceptionLayout.razor` | le bouton « Générer l'implantation » et l'alerte « Génération impossible : … » |
| `AliantWebS/Pages/LoadPages/ChargesSceniques.razor` | la page Charges, qui **est** le plan, et son « Import Excel » |
| `AliantWebS/Pages/LoadPages/ReportDialog.razor` | l'édition de l'étude Word |
| `AliantWebS/Pages/Settings.razor` | les trois réglages : « orientations orthogonales uniquement » (rôles Administrator et Developer), puis, dans « Developer Tools » et pour le seul rôle Developer, le classement des critères et le niveau de recherche |
| `AliantWebS/wwwroot/exemples/` | `Charges.xlsx`, `MotherGrill.xlsx`, `Palonnier.xlsx`, `Elements.xlsx`, `LISEZ-MOI.md` — les classeurs d'exemple, au format attendu, publiés en téléchargement |

#### Là où les règles sont tenues — `Tests/Shared.Tests/`

Les règles de ce manuel ne sont plus seulement écrites : elles sont vérifiées par un projet de tests qui fait
partie de la solution. `Tests/Shared.Tests/Shared.Tests.csproj` est en xunit, cible `net8.0`, référence
`Shared` — et **pas** le projet web : une règle qui aurait besoin d'un circuit Blazor pour être testée est une
règle rangée au mauvais endroit. Il est référencé par `AliantWebS.sln`, donc **il compile avec la solution** :
renommer une méthode citée dans ce manuel casse la compilation, pas seulement le texte.

| Fichier | Ce qu'il tient |
|---|---|
| `InventoryTests.cs` | l'inventaire et la table des charges admissibles, lus sur les vrais classeurs de la salle |
| `LoadTableTests.cs` | la lecture d'une valeur admissible : longueur immédiatement supérieure, calcul à moment égal, refus au-delà de la limite |
| `ExcelHeaderTests.cs` | la première ligne d'un classeur : en-tête ou première pièce, le fichier décide (§ 3.2) |
| `ManualTests.cs` | le manuel lui-même et le sous-ensemble Markdown qu'il emploie : rien ne doit atteindre le lecteur sous forme de balisage |
| `ManualWordTests.cs` | l'export Word du manuel : un document valide, et pas une phrase perdue entre les deux lectures |
| `ExampleFilesTests.cs` | les classeurs d'exemple publiés en téléchargement, au format que l'application attend |
| `GeometryTests.cs` | la grille : les coordonnées relevées, les écarts, les tolérances — les nombres contre lesquels tout le reste se mesure |
| `Engine/` | le moteur d'implantation lui-même : `AxesTests` (les deux repères et leur aller-retour), `GroupingTests` (les groupes en Y et en biais, la colinéarité), `PlacementTests` (ce que le plan de la salle donne : 6 palonniers, 7 lignes, 18 charges directes), `StockAndAssemblyTests` (le stock, les assemblages, la valeur admissible lue sur les vrais classeurs), `StoredLayoutTests` (ce qui est enregistré : pièces, accroches, noms, longueur des colonnes), `StressTests` (150 plans tirés au hasard, graine fixe), `ObjectiveTests` (le classement, ses clés, son analyse et son écriture), `BoundsTests` (les trois planchers), `ExactSearchTests` (la recherche exacte, sa preuve et son budget) |
| `Report/` | l'étude Word : `AbaqueCheckTests`, `LayoutMismatchTests`, `TrussInventoryTests`, `WordStudyTests`, `FigureTests` (les figures comme formes du document : un dessin et non une image, le texte lisible, le paquet valide), `TrussFigureTests` (les treillis : l'âme dans sa bande, des panneaux entiers, deux formes et non quarante), `ProductionPlanTests` (où le plan de la production est cherché, et où il ne l'est pas) |

**293 tests au vert** au moment où ces lignes sont écrites. Ils ne tournent **pas** à la compilation : ils se
lancent depuis l'explorateur de tests de Visual Studio ou par `dotnet test`.

### 14.2 Lexique

| Terme | Ce que c'est | Où il vit dans le code |
|---|---|---|
| **mother**, ligne de mother | un pont continu au-dessus d'une ligne de points, en travers des poutres, appuyé sur chaque poutre qu'il croise, sur une suite de travées chargées | `Models.Mother` ; `Core/MotherLines.cs` ; en base `MotherGridPalonnier` avec `Famille = true` |
| **palonnier** | un pont suspendu à des mothers, qui porte les charges ne tombant pas sur une ligne de points | `Models.Palonnier` ; en base `MotherGridPalonnier` avec `Famille = false` |
| palonnier **incliné** (biais) | palonnier dont les charges ne partagent ni x ni y ; ses extrémités sont les intersections avec les deux lignes de points encadrantes | `Models.PalonnierDirection.Biais` ; `GroupGenerator.GenerateBiaisGroups` |
| **travée** | l'intervalle entre deux appuis voisins : deux poutres pour un mother, deux accroches pour un palonnier. C'est l'unité de vérification | `Mother.BayCount`, `Mother.BayLength`, `Mother.BayLoadsOf` ; `ReportDataBuilder.BuildAbaqueChecks` |
| **point d'accroche** | un des 90 points de la charpente, nommé lettre + chiffre (E5, D3) | `CharpenteCoordinates.Points` ; `Geometry.Grid` ; `RiggingResult.GridLoads` |
| **poutre** | une des 10 poutres J..A, horizontales sur les plans. L'optimiseur n'en connaît que 9 (I..A) | `Geometry.Cols` ; `ReportDataBuilder.Beams` |
| **ligne de points** | une des 9 lignes 1..9, en travers des poutres | `Geometry.Rows`, `Geometry.RowYList` ; `Mother.Row` |
| **pré rigg** | l'ensemble des ponts montés — mothers et palonniers — et leur masse, que l'étude transmet aux points d'accroche | `TrussInventory` dans `ReportModel.cs` ; Tableau 2 de l'étude |
| **abaque** | la table des charges admissibles d'un modèle : une ligne par longueur, dans l'inventaire | `LoadTable` ; `Inventory.MotherAbaques`, `Inventory.PalonnierAbaques` ; `AbaqueCheck` |
| **charge ponctuelle au milieu** | la colonne de l'inventaire : la somme maximale des charges d'une travée, quelle que soit leur position. C'est une **charge utile**, hors poids propre du pont | `BarSpec.Capacity` ; `InventoryFiles.Columns` |
| **charge admissible** | la valeur retenue pour une portée : lue à la longueur immédiatement supérieure, ou calculée à moment égal au-delà de la plus grande longueur | `AllowedLoad`, `AllowedSource.Table` / `Computed` / `None` ; `LoadTable.At` |
| **portée** | la distance entre les deux appuis. C'est elle, jamais la longueur du pont, qui sert à lire la valeur admissible | `Palonnier.Span` ; `Mother.BayLength` |
| **composition**, pièces | les longueurs des pièces d'un pont assemblé, en centimètres entiers, la plus longue d'abord : `"[560,280]"` | `Assembly.Composition` ; `Helpers.TrussComposition` ; `MotherGridPalonnier.Composition` |
| **accrochage** | le fait qu'un palonnier pende d'une mother qu'il croise. Priorité : à la main > optimiseur > par défaut (accroché) | `PalonnierSupport` ; `HookRecord`, `PalonnierHooks`, `PalonnierHanging.Resolve` ; `MotherGridPalonnier.Accrochages` |
| **croisement non accroché** | une mother croisée dont le palonnier a été écarté parce qu'une travée aurait dépassé sa valeur | `Models.SkippedCrossing` ; `Optimizer.HangOnCrossings` ; feuille *Accrochages* |
| **porte-à-faux**, déport | la charge suspendue au-delà du dernier appui. L'inventaire n'en donne aucune valeur : jamais vérifié | `Mother.CantileverLoad` ; `ExcelExporter.Overhang` ; `AbaqueLoadCase.Cantilever` |
| **soulèvement** | une charge négative en un point d'accroche, produite par un porte-à-faux. Aucune valeur ne le couvre | `RiggingResult.GridLoads` négatif ; `AbaqueLoadCase.Uplift` |
| **charge directe** | une charge à moins de 8 cm d'une ligne de points, accrochée sur le mother sans palonnier | `Mother.DirectCharges` ; `Optimizer.Optimize` étape 1 |
| **groupe candidat** | un ensemble de charges qu'un palonnier pourrait porter, avec ses extrémités, sa portée et son poids | `Models.CandidateGroup` ; `GroupGenerator.Generate` |
| **axes des études** | origine E5, X le long des poutres vers le point 9, Y en travers vers J. Tout ce qu'une personne lit ou saisit | `Helpers.StudyAxes.ToStudy` |
| **axes de la grille** | origine E5, X en travers vers A, Y le long vers le point 1. Tout ce que la base et les calculs manipulent | `Helpers.StudyAxes.ToGrid` ; `CharpenteCoordinates` |
| **Point0** | l'origine des coordonnées stockées, E5 à l'Antarès. Lu par le calcul et l'étude ; **jamais** par l'optimiseur | `Event.Point0X`, `Event.Point0Y` |
| **stock**, Nombre | le nombre de pièces d'une ligne d'inventaire. Une ligne de Nombre = 0 ne donne qu'une valeur et n'est jamais une pièce | `BarSpec.StockInit`, `BarSpec.Stock` ; `PieceStock.Rows` |
| **poids propre** | la masse d'une pièce, colonne de l'inventaire. Vide = inconnu, jamais zéro | `BarSpec.WeightKg` ; `Assembly.WeightKg` ; `TrussCatalog` |
| **réserve sur les données** | ce qui limite la portée de l'étude sans l'empêcher de conclure | `RigReport.Warnings` |
| **vérification non effectuée** | un contrôle que l'étude n'a pas pu faire. Bloque la conclusion — ce n'est pas un succès | `RigReport.VerificationGaps` ; `RigReport.IsConclusive` |
| **implantation** | le résultat complet : lignes de mother, palonniers, accrochages et charges posées, tels qu'ils sont enregistrés pour un scénario | `RiggingResult` en mémoire ; `MotherGridPalonnier` + `ChargeMp` en base |
| **contrainte dure** | une règle qu'aucun gain ne rachète. Une implantation qui en casse une n'est pas une moins bonne implantation : ce n'en est pas une | `Objective.HardConstraints` ; chapitre 15 |
| **classement** | l'ordre des critères, lu de haut en bas ; le premier qui sépare deux implantations décide seul. Enregistré sous forme de clés | `Objective.DefaultRanking`, `Parse`, `Compare` ; clé `optimizer.objective.order` |
| **coût mesuré** | les neuf quantités d'une implantation terminée, mesurées une fois à la fin de la construction | `LayoutCost` ; `RiggingResult.Cost` ; feuille *Synthèse* |
| **plancher**, borne | le minimum qu'aucune implantation d'un plan ne peut passer. Jamais la preuve qu'on peut l'atteindre | `LayoutBound`, `LayoutGap` ; `Bounds.cs` ; chapitre 16 |
| **optimum prouvé** | parmi toutes les couvertures des charges par les groupes candidats, aucune n'est préférée par ce classement. Rien de plus | `SearchOutcome.Proven` ; `ExactSearch.Run` ; § 16.5 |


## Chapitre 15 — Ce que l'optimiseur cherche, et qui le décide

Ce chapitre n'ajoute aucune règle de placement. Il répond à une question que ce manuel ne savait pas
répondre : **entre deux implantations qui tiennent toutes les deux, laquelle vaut mieux ?** Jusqu'à peu, la
réponse vivait dans trois endroits du code avec des nombres ajustés à la main — un `0,3` ajouté à un compte
dans `SelectGreedy`, un `0,1` dans le tri d'à côté, la comparaison d'assemblages de `PieceStock` — et deux
d'entre eux ne disaient pas la même chose. Personne ne pouvait dire ce que ces nombres voulaient dire, pour
une raison simple : ils ne voulaient rien dire. C'étaient les plus petites valeurs qui donnaient, ce jour-là,
l'implantation que quelqu'un voulait.

Ils ont été remplacés par un fichier qui n'implante rien et n'interdit rien : `Core/Objective.cs`. C'est
l'index des règles — chacune nommée à côté du code qui l'applique — et l'étalon avec lequel les implantations
finies sont mesurées. Aucun Blazor, aucun Entity Framework, aucune lecture de fichier : un lecteur peut y
vérifier les règles sans base de données devant lui.

### 15.1 Les quatre contraintes dures

Ce ne sont pas des critères, et elles n'ont pas de poids. Une implantation qui en casse une n'est pas une
implantation moins bonne, **ce n'est pas une implantation**. `Objective.HardConstraints` les nomme et
désigne, pour chacune, le code qui l'applique — de sorte qu'un rigger qui lit les règles et un développeur
qui lit le code lisent les quatre mêmes règles.

| Contrainte | Ce qu'elle interdit | Où elle est appliquée |
|---|---|---|
| Toute charge portée | chaque charge est portée par un palonnier ou par une ligne de mother ; aucune n'est laissée de côté | `Pipeline.GenerateLayout`, la garde sur `RiggingResult.Uncovered` (§ 10.1) |
| Charge admise jamais dépassée | aucune travée de mother ni aucun palonnier ne porte plus que la charge admise de son modèle à sa portée | `MotherLines.Size`, `Optimizer.AssemblyFor` / `SelectGreedy`, `Optimizer.HangOnCrossings` (chapitres 4, 8, 9) |
| Jamais plus de pièces que le stock | chaque pièce assemblée sort de l'inventaire et n'y est comptée qu'une fois | `PieceStock.Take` (§ 5.3) |
| Orientations orthogonales uniquement | quand le réglage est actif, aucun palonnier incliné n'est proposé ni placé | `OptimizerOptions.OrthogonalOnly` → `GroupGenerator.Generate(allowBiais:)` (§ 11.1) |

La deuxième est la règle pour laquelle tout l'outil existe. La première est celle qui fait refuser un
enregistrement : la salle préfère ne rien recevoir plutôt qu'un plan à moitié juste, puisque l'enregistrement
**remplace** l'implantation précédente.

### 15.2 Un classement, pas une somme pondérée

Les cinq critères sont comparés **l'un après l'autre**, dans l'ordre. Le premier qui sépare deux
implantations décide seul ; ceux d'en dessous n'entrent jamais en jeu. C'est ce que fait `Objective.Compare`,
et c'est délibéré.

Pourquoi pas une somme pondérée ? Parce que des quantités qu'on additionne finissent en constantes ajustées :
combien de kilos valent un palonnier ? combien de mètres valent une pièce ? Il n'existe aucune réponse
défendable, et c'est très exactement ainsi que le `0,3` et le `0,1` sont nés — après quoi ni l'un ni l'autre
ne pouvait plus être expliqué ni changé sans risque. Comparés un critère après l'autre, la préférence se
change en déplaçant **une ligne** du tableau `DefaultRanking`, et rien d'autre ne bouge dans le code.

Le prix de ce choix est réel et il faut le connaître : un critère bas dans l'ordre ne rachète **jamais** un
critère haut, si petit que soit l'écart. Une implantation avec un palonnier de plus perd contre une
implantation avec un palonnier de moins, même si elle pèse 400 kg de moins. C'est une décision d'entreprise,
pas une décision d'algorithme.

Deux figures identiques ne le sont jamais au dernier bit : une même masse sommée dans un autre ordre diffère
à la dernière décimale. Chaque critère porte donc la distance à laquelle deux valeurs sont **égales**, et le
critère suivant décide alors : `MassToleranceKg` = **0,001 kg**, `LengthToleranceM` = **0,001 m**,
`UseTolerance` = **1e-6** de la charge admise. Un gramme, un millimètre ou un millionième d'une charge admise
ne sont pas une raison de préférer une implantation à une autre.

### 15.3 Les cinq critères, et ce que chacun coûte dans la salle

Voici l'ordre en vigueur quand rien n'est enregistré, celui du tableau `DefaultRanking` :

| # | Clé | Critère | Sens | Ce que cela coûte pour de vrai |
|---|---|---|---|---|
| 1 | `palonniers` | palonniers | moins | chaque palonnier est un accrochage : un treillis à monter, deux points de rigging au moins, un nœud de travail en hauteur, et sa propre masse ajoutée aux mothers qui le portent. C'est le coût que l'équipe sent |
| 2 | `masse` | masse de poutre suspendue (kg) | moins | la masse qui arrive réellement sur les poutres bois. C'est ce que le calcul charpente vérifie, et le seul chiffre qu'une salle peut refuser tout net |
| 3 | `pieces` | pièces assemblées | moins | chaque pièce est du travail d'assemblage au sol, et chaque jonction entre deux pièces est une hypothèse : qu'elle a été faite, bien faite, et faite avec les broches avec lesquelles elle a été dessinée (§ 5.1) |
| 4 | `longueur` | longueur de poutre (m) | moins | le matériel sorti du stock : les mètres qui quittent l'inventaire et ne serviront pas ailleurs sur le même spectacle. C'est la longueur **assemblée**, pas la longueur tracée — une pièce de 5,6 m posée sur 4,2 m coûte 5,6 m au stock |
| 5 | `marge` | marge sur la travée la plus chargée | plus | la réserve sur le treillis le plus tendu, pour ce que la production ajoutera après la signature de l'étude : un moteur remplacé par un plus lourd, une barre de lumières qui a grossi. En dernier, parce que c'est du confort : toute implantation arrivée jusque-là respecte déjà chaque charge admise |

> **Les critères 1 et 2 peuvent se contredire, et ce n'est pas un artefact du modèle.** Un palonnier incliné
> qui porte trois charges, c'est un accrochage au lieu de trois ; mais le long treillis qu'il réclame peut
> peser plus que les trois courts qu'il remplace. L'ordre ci-dessus compte l'accrochage d'abord — moins
> d'accrochages, c'est moins d'heures sur site, moins de points de rigging, moins de choses à rater en
> hauteur — et accepte les kilos en plus quand ils viennent. Un développeur qui pense l'inverse pour cette
> salle inverse les deux lignes sur la page Paramètres (§ 11.3) ; il ne touche pas au code.

Les **clés** sont l'identité d'un critère et ne changent jamais : c'est ce qui est écrit dans la base de
chaque installation. Le **nom** est un libellé, il peut être réécrit le jour où un rigger demande du meilleur
français.

Enfin, chaque exécution garde ce sous quoi elle a été bâtie : `RiggingResult.Search` porte le classement
appliqué sous sa forme enregistrée (`palonniers,masse,pieces,longueur,marge`), le niveau de recherche, le
nombre d'implantations examinées, le temps et la mention d'optimalité. Une étude qui ne peut pas dire sous
quel objectif elle a été construite n'est pas traçable.

### 15.4 Ce que `LayoutCost` mesure, quantité par quantité

Une implantation terminée est mesurée **une seule fois**, à la fin de sa construction (`Optimizer.Build`), et
la mesure est portée sur le résultat. C'est ce qui fait que la console, la feuille *Synthèse* du classeur et
une comparaison entre deux exécutions citent le même chiffre, calculé de la même façon.

| Quantité | Ce qu'elle compte | Dans le classement |
|---|---|---|
| `Palonniers` | les palonniers suspendus | critère 1 |
| `Lines` | les lignes de mother tracées | mesurée seulement |
| `Pieces` | les pièces de stock assemblées, tous rôles confondus. Un pont qui n'a pas été assemblé compte pour un | critère 3 |
| `DrawnMetres` | la longueur **tracée** sur le plan : la portée de chaque palonnier et la longueur de chaque ligne | mesurée seulement |
| `AssembledMetres` | la longueur **sortie du stock** : la longueur propre des pièces, surlongueur comprise | critère 4 |
| `TrussMassKg` | la masse de poutre suspendue aux poutres bois. Une pièce de masse inconnue compte pour zéro ici ; c'est l'étude qui le signale (§ 13.3) | critère 2 |
| `WorstBayUse` | le plus grand rapport charge / admissible sur toutes les travées et tous les palonniers. 0,37 veut dire que le treillis le plus tendu emploie 37 % de ce que l'inventaire lui autorise. Une travée sans valeur admise n'est pas un rapport : elle est laissée de côté, et c'est l'étude qui la signale | critère 5, en marge : 1 − `WorstBayUse` |
| `ChargesDirect` | les charges pendues directement sur une ligne de mother | mesurée seulement |
| `ChargesOnPalonniers` | les charges pendues d'un palonnier | mesurée seulement |

Quatre des neuf quantités sont donc **mesurées sans être classées**. C'est voulu : elles servent à lire une
implantation et à comparer deux exécutions, pas à en préférer une.

### 15.5 La référence chiffrée : ce que produit le classement en vigueur

Ces chiffres ont été mesurés, pas déduits. Ils ont été relevés **avant et après** tout le travail décrit dans
ce chapitre et le suivant, sur **153 plans**, et ils sont **identiques des deux côtés, plan par plan** : le
placement glouton n'a pas bougé d'un millimètre. C'est ce qui autorisait à retirer le `0,3` et le `0,1`.

**Le plan de la salle** — 24 charges, 6 210 kg, le plan du dossier partagé `_common/data` :

| Quantité | Valeur |
|---|---|
| palonniers | 6 |
| lignes de mother | 7 |
| pièces assemblées | 32 |
| longueur tracée | 115,39 m |
| longueur assemblée | 125,2 m |
| masse de poutre suspendue | 1 632,4 kg |
| travée la plus chargée | 0,366 de la charge admise, soit une marge de 0,634 |

La longueur tracée se refait à la main avec les chiffres du § 8.1 : les sept lignes de mother font 93,438 m,
les six palonniers ont pour portées 3,600 + 3,600 + 3,650 + 3,650 + 3,700 + 3,750 = 21,950 m, et
93,438 + 21,950 = **115,388 m**.

**Les classeurs d'exemple livrés** — 8 charges, 2 516 kg, `wwwroot/exemples` : 2 palonniers, 5 lignes de
mother, 17 pièces, 64,44 m tracés, 817 kg de poutre suspendue.

**150 plans tirés au hasard** : 146 entièrement placés. Moyennes sur ces plans : 6,81 palonniers, 9,59 lignes
de mother, 39,5 pièces, 139,6 m tracés, 2 156 kg de poutre. La travée la plus chargée observée sur l'ensemble
monte à **0,994** — c'est-à-dire à 6 millièmes de la charge admise. Les quatre plans restants ne sont pas
entièrement placés ; ce relevé ne dit pas pourquoi, et c'est le message de refus de chacun qui le dit
(§ 10.1, § 13.1).

C'est la référence contre laquelle une modification future se lit. Un changement du moteur qui déplace l'un
de ces nombres a changé les implantations que la salle reçoit — et doit dire lequel, de combien, et pourquoi.

*Source : `Shared/ScenarioHelpers/Core/Objective.cs`, en entier — l'en-tête du fichier porte l'argument, le
tableau `DefaultRanking` porte l'ordre ; `Shared/ScenarioHelpers/Core/Optimizer.cs`, `Build` (la mesure) et
`Report` (la sortie console) ; `Shared/ScenarioHelpers/ExcelExporter.cs`, la feuille Synthèse ;
`Shared/ScenarioHelpers/Models/Models.cs`, `OptimizerOptions` et `SearchOutcome`.*

## Chapitre 16 — Les bornes, la recherche exacte, et ce qu'elles prouvent

Le chapitre 15 dit ce qu'on cherche. Celui-ci dit ce qu'on sait de la réponse : d'abord un plancher calculé à
chaque exécution, ensuite — sur demande — une recherche qui parcourt les autres réponses possibles et dit
quand elle a tout vu.

### 16.1 Ce qu'une borne inférieure est, et ce qu'elle n'est pas

Une borne inférieure **ne prouve pas qu'une meilleure implantation existe**, et ce n'est pas un objectif.
Elle dit une seule chose : *aucune implantation de ce plan ne peut faire moins que ce chiffre.*

- Une réponse gloutonne posée **sur** la borne est optimale sur ce critère, et il n'y a plus rien à chercher.
- Une réponse loin **au-dessus** de la borne peut parfaitement être la meilleure qui existe : la borne est
  alors simplement faible.

C'est la règle qui gouverne tout le fichier `Core/Bounds.cs` : **une borne ne suppose jamais.** Partout où la
géométrie laisse un choix — une charge qui pourrait pendre de l'une ou l'autre de deux lignes de points, un
palonnier dont la portée n'est pas fixée —, ce choix ne contribue rien. Supposer une seule fois la moins
chère des deux possibilités, ce serait dire à une étude qu'elle est plus près de l'optimum qu'elle ne l'est.
C'est la seule erreur qu'une borne n'a pas le droit de commettre : être trop basse n'est qu'inutile, être
trop haute est faux.

Les trois bornes sont donc délibérément faibles plutôt que malignes. Ce sont des fonctions pures sur les
entrées mêmes de l'optimiseur : aucune pièce prise, aucun résultat lu, rien de placé.

### 16.2 Les trois planchers, et comment chacun se calcule

**Les mètres de mother.** Une ligne de points n'a besoin d'une ligne de mother que si quelque chose **doit**
y pendre, et ce qui doit y pendre, c'est une charge assise sur cette ligne, à moins de `Geometry.TOL`
(§ 6.1) : où que l'implantation mette ses palonniers, cette charge-là pendra de cette ligne-là. La borne
n'invente donc rien : elle demande à `MotherLines.Plan` lui-même de planifier les lignes avec ces seules
demandes forcées, et somme leurs longueurs. Une charge qui n'est **pas** à moins de `TOL` d'une ligne de
points pendra d'un palonnier dont l'implantation est libre de poser les extrémités sur l'une ou l'autre des
deux lignes qui l'encadrent, et n'importe où le long de celles-ci : elle ne force ni ligne ni travée, et ne
contribue rien.

**Le nombre de palonniers.** Une charge à plus de `TOL` de toute ligne de points ne peut pas pendre d'une
ligne : il lui faut un palonnier. On divise donc le nombre de ces charges par le plus grand nombre de charges
qu'**un seul** palonnier pourrait porter au vu de la seule géométrie — celles qui partagent un x à `TOL`
près, ou, si les inclinés sont autorisés, celles qui sont à moins de `ColinearTol` d'un même axe —, arrondi
au supérieur. Cette borne ignore complètement le stock, et le dit : aucune pièce n'est peut-être assez longue
pour traverser ce groupe, aucun modèle assez fort pour le porter, auquel cas la vraie implantation demande
**plus** de palonniers que ce chiffre — la direction dans laquelle un plancher a le droit de se tromper. La
taille du groupe est comptée généreusement pour la même raison : surestimer ce qu'un palonnier peut porter
abaisse le plancher, et un plancher abaissé ne peut jamais surestimer.

**Le nombre de pièces.** Pour chaque treillis que la géométrie force — chaque ligne de mother du premier
plancher, chaque palonnier du second —, la portée divisée par la plus longue pièce du stock de ce rôle,
arrondie au supérieur. Assembler avec la plus longue pièce disponible donne le moins de pièces possible, donc
personne ne peut faire mieux ; une vraie implantation, elle, choisit son modèle pour ce qu'une travée doit
porter, pas pour sa longueur, et en emploie donc davantage. La portée d'un palonnier n'est pas forcée, mais
ses deux extrémités tombent sur deux lignes de points différentes : elle vaut donc au moins l'écart des deux
lignes de points les plus rapprochées, 3,600 m (§ 2.3). Sans stock de palonniers pour se mesurer, un
palonnier compte pour une pièce — le plancher des planchers.

**Sur le plan de la salle**, les trois planchers valent : **au moins 3 palonniers, 18 pièces et 64,65 m de
ligne de mother.** En face des chiffres du § 15.5 — 6 palonniers, 32 pièces, 115,39 m tracés — l'écart est de
3 palonniers, 14 pièces et 50,74 m.

> L'écart en mètres se lit avec précaution, et le classeur le répète à côté du chiffre. `DrawnMetres` compte
> toute l'implantation, lignes de mother **et** palonniers ; le plancher, lui, ne borne que les lignes de
> mother, faute de plancher honnête pour la portée d'un palonnier. Ces 50,74 m sont donc une **limite haute**
> de ce qui pourrait être gagné, jamais un objectif : l'essentiel est de la longueur de palonnier qu'il
> fallait bien tracer. Les deux autres écarts, eux, bornent exactement ce que l'implantation compte.

Le plancher est calculé **en dernier** dans `Optimize`, et ce n'est pas un détail : il planifie des lignes
jetables, et une `Mother` prend au passage un identifiant au compteur. Les vraies lignes doivent avoir pris
les leurs d'abord.

Il est imprimé sur la console après chaque génération, et écrit dans la feuille *Synthèse* du classeur, avec
l'écart en face (§ 13.2).

### 16.3 La recherche exacte : ce qu'elle parcourt

Une charge qui n'est pas assise sur une ligne de points doit pendre d'un palonnier, et un palonnier porte un
**groupe** de ces charges — `GroupGenerator` énumère tous les groupes qu'un palonnier pourrait porter
(chapitre 7). Choisir l'implantation, c'est donc choisir un ensemble de groupes qui couvre chacune de ces
charges **exactement une fois** : aucune laissée de côté, aucune pendue à deux palonniers.

Le glouton prend le plus gros groupe qu'il peut encore assembler, puis le suivant, et ne revient jamais en
arrière. Rien ne lui dit que deux groupes moyens auraient battu le gros qu'il a pris en premier. La recherche
exacte, elle, les essaie tous : pour chaque couverture complète, elle construit l'implantation **exactement
comme le glouton construit la sienne** — le même `Optimizer.Build`, les mêmes stocks, les mêmes lignes de
mother et les mêmes croisements —, la mesure (`LayoutCost`) et ne la garde que si le classement la préfère.

Trois détails de mise en œuvre méritent d'être connus, parce qu'ils expliquent que la comparaison soit
honnête :

1. **Le stock est rembobiné entre deux branches.** Une branche qui assemble trois palonniers sort ces pièces
   de l'inventaire ; sans rembobinage, la branche suivante chercherait dans un stock que la première a vidé,
   et s'entendrait dire à tort que son propre groupement n'est pas assemblable. Un `StockMark` pris avant une
   branche et rendu après elle fait voir à chacune le même inventaire.
2. **Les identifiants repartent de 1 à chaque tentative**, pour que l'implantation retenue se lise comme
   n'importe quelle autre — P1, P2, M1 — et non avec les numéros du millième essai.
3. **L'ordre de la marche** : à chaque étape, on décide pour la charge qui a le **moins** de groupes encore
   utilisables. Une charge que seuls deux palonniers pourraient porter coupe la marche en deux ; la laisser
   pour plus tard ferait redécouvrir la même impasse des milliers de fois.

### 16.4 La garantie, l'élagage et le budget

**La garantie, et c'est la seule promesse dont un utilisateur a besoin avant d'allumer le réglage : la
recherche part de l'implantation gloutonne et ne peut la remplacer que par une que le classement préfère.**
Le glouton est le tenant du titre avant qu'aucune branche ne soit ouverte ; un prétendant ne le remplace
qu'après avoir été construit, mesuré et comparé ; et tout ce qui tourne mal — une branche qui ne se construit
pas, une exception, le budget épuisé — laisse le tenant en place. Allumer `exact` ne peut donc pas dégrader
un plan.

L'unique exception va dans l'autre sens : une implantation sans reproche remplace une implantation gloutonne
qui laissait une charge en l'air ou dépassait une charge admise, quel que soit le classement. Ce sont les
contraintes dures qui parlent alors, pas l'ordre des critères — et il le faut, car le classement ne compte ni
les charges portées ni les dépassements : une implantation qui porte moins **paraîtrait** moins chère.

**L'élagage.** Le nombre de palonniers d'une implantation ne peut que croître à mesure que la marche
descend : chaque groupe ajouté est un palonnier de plus, et les charges encore à couvrir en demandent au
moins un autre. Dès que la branche en main réclame déjà plus de palonniers que la meilleure implantation
trouvée, aucune façon de la terminer ne peut gagner sur le premier critère, et toute la branche est
abandonnée sans être regardée. Rien n'est perdu : un critère plus bas dans le classement ne renverse jamais
le premier.

Cette coupe a **deux conditions**, et elles sont dans le code :

- le classement en vigueur doit effectivement commencer par les palonniers, à la baisse. Un développeur qui a
  déplacé cette ligne (§ 11.3) a changé le sens du mot « pire » ; la coupe ne serait plus valable, et elle
  est simplement abandonnée — la recherche se contente alors de son budget ;
- la meilleure implantation en main doit porter **toutes** les charges. Comparer un nombre de palonniers à
  celui d'une implantation qui en laisse tomber couperait à tort.

**Le budget.** Par défaut **2 000 ms** et **20 000 nœuds**, vérifiés à chaque nœud pour qu'aucune branche ne
puisse les manquer. Les deux ont chacun leur raison : le temps, parce qu'une génération est un clic sur une
page, et qu'une page qui réfléchit deux secondes travaille tandis qu'une page qui réfléchit une minute est en
panne ; le compte de nœuds, parce que l'horloge seule laisserait une machine dix fois plus rapide faire dix
fois plus de travail et rendre une **autre** implantation pour le même plan — ce qu'une étude ne saurait pas
expliquer. Le compte rend la recherche reproductible quelle que soit la machine.

### 16.5 Ce que « optimum prouvé » dit, et ce qu'il ne dit pas

La phrase est annoncée uniquement quand l'espace entier a été parcouru, et elle dit exactement ceci :

> **parmi toutes les façons de couvrir ces charges avec ces groupes candidats, aucune ne donne une
> implantation que ce classement préfère à celle qui est rendue.**

Ce qu'elle ne dit **pas** : qu'aucune meilleure implantation n'existe au monde. Une autre suite de lignes de
mother, un autre choix de pièces, un groupe qu'aucun générateur n'a proposé, sont tous hors de ce qui a été
parcouru. Elle ne dit rien non plus d'un autre classement que celui qui était en vigueur.

Et elle n'est pas avancée à la légère. Elle est **fausse** dès que : le budget a arrêté la marche ; une
branche n'a pas pu être construite ; une charge qu'aucun groupe ne couvre rend toute couverture complète
impossible ; ou l'implantation rendue laisse une charge non portée ou dépasse une charge admise — la marche a
eu beau tout parcourir, elle n'a rien trouvé de mieux, ce qui n'est pas la même chose qu'avoir trouvé la
meilleure. Une recherche qui n'est pas sûre le dit.

C'est la ligne `Recherche` de la console, celle que `SearchOutcome.Describe` écrit :

```
Recherche exacte : 2 implantation(s) examinée(s) en 4 ms — optimalité prouvée pour ce classement.
Classement : palonniers,masse,pieces,longueur,marge.
```

Au niveau `bounds`, la même ligne dit « Placement glouton » et « aucune optimalité n'est prouvée » — sans
avancer de raison, parce qu'il n'y en a pas une seule.

### 16.6 Ce que la recherche exacte rapporte réellement, mesuré

La question qui compte n'est pas « est-ce que ça marche » mais « est-ce que ça change quelque chose ».
Réponse mesurée :

**Sur le plan de la salle** (24 charges) : l'implantation rendue est **identique** à celle du glouton, sauf
sur la marge de la travée la plus chargée, qui passe de **0,634 à 0,636**. Optimum **prouvé**,
**2 implantations examinées**, **4 ms**.

**Sur 60 plans tirés au hasard** :

| Résultat | Nombre de plans |
|---|---|
| meilleure que le glouton | 6 |
| identique au glouton | 54 |
| moins bonne que le glouton | 0 |

Optimum prouvé sur **59 plans sur 60**. **1 ms** en moyenne, **3 ms** au pire.

Et le point le plus instructif : **le gain porte toujours sur la marge**, jamais sur les palonniers, jamais
sur la masse, ni sur les pièces, ni sur la longueur. Autrement dit, sur ces plans-là, le glouton trouvait
déjà le bon nombre de palonniers et le bon métrage ; ce que la recherche exacte a récupéré, ce sont quelques
millièmes de charge admise sur le treillis le plus tendu.

### 16.7 Lequel des deux modes employer

La conclusion honnête, et elle n'est pas celle qu'on attend : **la valeur du mode exact, c'est la preuve, pas
le gain.**

- **`bounds`, le défaut**, est le bon choix pour le travail courant. Il est instantané, il donne la même
  implantation dans la quasi-totalité des cas, et il annonce déjà le plancher — donc de combien, au plus,
  la réponse pourrait être améliorée.
- **`exact`** vaut d'être allumé quand on a besoin d'écrire quelque chose de défendable : *sur ce plan, sous
  ce classement, aucune autre façon de grouper les charges ne donne mieux.* C'est une phrase qu'on peut
  mettre devant un ingénieur, et aucune quantité de placements gloutons ne la produira.

Il vaut aussi d'être allumé sur un plan que le glouton place mal, ou pas du tout : la garantie du § 16.4 dit
qu'on ne risque rien à essayer. Ce qu'il faut savoir, en revanche, c'est que la preuve n'est pas toujours au
rendez-vous — un plan sur les soixante tirés au hasard ne l'a pas obtenue, et le § 16.5 donne la liste des
raisons possibles, le budget en tête. La réponse est alors la meilleure trouvée, sans preuve, et la console
le dit dans ces mots-là.

*Source : `Shared/ScenarioHelpers/Core/Bounds.cs`, en entier — l'en-tête porte la règle « une borne ne
suppose jamais » ; `Shared/ScenarioHelpers/Core/ExactSearch.cs`, l'en-tête puis `Run`, `Cover`, `Examine`,
`Keep` et `StockMark` ; `Shared/ScenarioHelpers/Core/Optimizer.cs`, `Optimize` (étape 4 et le calcul du
plancher) ; `Shared/ScenarioHelpers/Models/Models.cs`, `SearchLevel`, `SearchOutcome` et les budgets
d'`OptimizerOptions` ; `Shared/ScenarioHelpers/ExcelExporter.cs`, la feuille Synthèse.*
