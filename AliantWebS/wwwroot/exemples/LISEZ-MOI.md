# Fichiers d'entrée — exemples

Ces quatre classeurs montrent le format attendu par l'application. Les valeurs sont des exemples :
remplacez-les par les vôtres. Les fichiers sont lus dans leur **première feuille**.

> Les deux inventaires ci-dessous sont des **extraits**, pas l'inventaire du parc. Ne les téléversez pas
> tels quels dans un scénario : les références absentes disparaîtraient du stock utilisable.

**La ligne d'en-tête est facultative.** Avec en-tête, les colonnes sont reconnues par leur nom (majuscules,
accents et unités entre parenthèses sont ignorés, l'ordre des colonnes est libre). Sans en-tête, les colonnes
sont lues dans l'ordre indiqué ci-dessous, à partir de la première ligne.

**Coordonnées** : en mètres, dans les axes des études — origine au Point0 de l'événement (E5 à l'Antarès),
**X** le long des poutres, positif vers le point 9, **Y** en travers des poutres, positif vers la poutre J.
Ce sont les axes affichés partout : page Charges, plan, étude Word, export Excel et DXF.

**Masses et charges** : en kilogrammes.

---

## Charges.xlsx — le plan de charges

`Nom | X (m) | Y (m) | Charge (kg)`

Une ligne par charge. Le nom identifie la charge partout ensuite (implantation, calcul, étude) : il doit être
unique et non vide.

Ce fichier s'importe sur la page **Charges** (bouton « Import Excel »). C'est la page Charges qui fait foi :
« Générer l'implantation » place les charges de la page, pas celles d'un fichier. Importez, vérifiez, puis
générez.

## MotherGrill.xlsx — l'inventaire des mothers

## Palonnier.xlsx — l'inventaire des palonniers

Mêmes colonnes dans les deux fichiers :

`Modèle | Longueur | Nombre | Poids propre | Charge ponctuelle au milieu | Matériau | Marque | Usage`

- **Longueur** : la longueur d'une pièce, en mètres.
- **Nombre** : combien de pièces sont en stock. L'implantation n'en utilise jamais plus.
  **Nombre = 0** : la ligne ne sert qu'au calcul des charges admissibles, aucune pièce n'est disponible.
- **Poids propre** : la masse d'**une** pièce, en kg.
- **Charge ponctuelle au milieu** : la charge admissible, en kg. La somme des charges situées entre deux points
  d'accrochage voisins est comparée à cette valeur, prise à la longueur **immédiatement supérieure** à la portée.
- **Matériau**, **Marque**, **Usage** sont facultatifs (`Usage` : `MotherGrid` ou `Palonnier`).

Ces deux fichiers se déposent dans les fichiers du scénario (« Gérer les fichiers »). Un fichier absent du
scénario est lu dans le dossier partagé `_common/data`.

## Elements.xlsx — mothers et palonniers déjà dessinés

`Nom | Type | X1 (m) | Y1 (m) | X2 (m) | Y2 (m) | Composition`

À importer depuis la page **MotherGrid**, sur la grille des MotherGrids ou sur celle des Palonniers. Les deux
extrémités sont données dans les axes des études.

- **Type** : le modèle, tel qu'il figure dans l'inventaire.
- **Composition** (facultatif) : les pièces mises bout à bout, en mètres, séparées par `+` — par exemple
  `5,6+2,8`, ou `2x5,6+2,8`. Sans composition, l'élément compte pour une seule pièce de sa longueur.

Un mother est posé en travers des poutres (X identique aux deux extrémités) ; un palonnier va d'une ligne de
mother à une autre (Y identique aux deux extrémités).
