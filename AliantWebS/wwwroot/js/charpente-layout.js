// Page Charpente : la colonne de gauche (plan + compte rendu) prend la hauteur du contenu de droite
// (grille + tableau des taux), pour que les deux bas de page soient alignés. Suivi par ResizeObserver :
// la hauteur de droite change après un calcul, un changement de page du tableau, un redimensionnement.

const MIN_HAUTEUR = 600; // px : en dessous, le plan et le compte rendu deviennent illisibles

export function alignerHauteur(gauche, droite) {
    if (!gauche || !droite) return null;

    const ajuster = () => {
        const bas = droite.getBoundingClientRect().bottom;
        const haut = gauche.getBoundingClientRect().top;
        gauche.style.height = Math.max(MIN_HAUTEUR, Math.round(bas - haut)) + "px";
    };

    const observer = new ResizeObserver(ajuster);
    observer.observe(droite);
    window.addEventListener("resize", ajuster);
    ajuster();

    return {
        dispose: () => {
            observer.disconnect();
            window.removeEventListener("resize", ajuster);
        }
    };
}

export function liberer(handle) {
    handle?.dispose();
}
