// Fonction pour créer un indicateur de jauge circulaire
function createGauge(canvasId, value, max = 10, title = '', colors = { good: '#4CAF50', warning: '#FF9800', danger: '#F44336' }) {
    console.log(`Creating gauge ${canvasId} with value ${value}, max ${max}`);
    
    const canvas = document.getElementById(canvasId);
    if (!canvas) {
        console.error(`Canvas element ${canvasId} not found`);
        return false;
    }
    
    const ctx = canvas.getContext('2d');
    if (!ctx) {
        console.error(`Could not get 2D context for ${canvasId}`);
        return false;
    }
    
    const centerX = canvas.width / 2;
    const centerY = canvas.height / 2;
    const radius = Math.min(canvas.width, canvas.height) / 2 - 15;
    
    // Effacer le canvas
    ctx.clearRect(0, 0, canvas.width, canvas.height);
    
    // Calculer l'angle basé sur la valeur (limiter la valeur au maximum)
    const clampedValue = Math.min(Math.max(value, 0), max);
    const angle = (clampedValue / max) * Math.PI; // Demi-cercle
    
    // Dessiner l'arc de fond
    ctx.beginPath();
    ctx.arc(centerX, centerY, radius, Math.PI, 2 * Math.PI);
    ctx.lineWidth = 8;
    ctx.strokeStyle = '#E0E0E0';
    ctx.stroke();
    
    // Dessiner l'arc de valeur avec couleur dynamique
    let color = colors.good;
    if (clampedValue >= max * 0.8) color = colors.danger;
    else if (clampedValue >= max * 0.6) color = colors.warning;
    
    if (angle > 0) {
        ctx.beginPath();
        ctx.arc(centerX, centerY, radius, Math.PI, Math.PI + angle);
        ctx.lineWidth = 8;
        ctx.strokeStyle = color;
        ctx.stroke();
    }
    
    // Dessiner l'aiguille
    const needleAngle = Math.PI + angle;
    const needleLength = radius - 5;
    ctx.beginPath();
    ctx.moveTo(centerX, centerY);
    ctx.lineTo(
        centerX + Math.cos(needleAngle) * needleLength,
        centerY + Math.sin(needleAngle) * needleLength
    );
    ctx.lineWidth = 2;
    ctx.strokeStyle = '#333';
    ctx.stroke();
    
    // Dessiner le centre
    ctx.beginPath();
    ctx.arc(centerX, centerY, 4, 0, 2 * Math.PI);
    ctx.fillStyle = '#333';
    ctx.fill();
    
    // Dessiner les graduations principales (adaptées au maximum)
    const numGraduations = Math.min(max, 10); // Maximum 10 graduations
    const stepSize = max / numGraduations;
    
    for (let i = 0; i <= numGraduations; i++) {
        const gradAngle = Math.PI + (i / numGraduations) * Math.PI;
        const startRadius = radius - 3;
        const endRadius = radius + 3;
        
        ctx.beginPath();
        ctx.moveTo(
            centerX + Math.cos(gradAngle) * startRadius,
            centerY + Math.sin(gradAngle) * startRadius
        );
        ctx.lineTo(
            centerX + Math.cos(gradAngle) * endRadius,
            centerY + Math.sin(gradAngle) * endRadius
        );
        ctx.lineWidth = 1;
        ctx.strokeStyle = '#666';
        ctx.stroke();
        
        // Ajouter les nombres (adaptés au maximum)
        const graduationValue = (i * stepSize).toFixed(stepSize < 1 ? 1 : 0);
        ctx.fillStyle = '#333';
        ctx.font = '9px Arial';
        ctx.textAlign = 'center';
        ctx.fillText(
            graduationValue,
            centerX + Math.cos(gradAngle) * (radius + 10),
            centerY + Math.sin(gradAngle) * (radius + 10) + 3
        );
    }
    
    // Dessiner la valeur au centre
    ctx.fillStyle = color;
    ctx.font = 'bold 14px Arial';
    ctx.textAlign = 'center';
    ctx.fillText(clampedValue.toFixed(1), centerX, centerY - 5);
    
    console.log(`Successfully created gauge ${canvasId} with max ${max}`);
    return true;
}

// Fonction pour rendre une jauge individuelle
window.renderSingleGauge = function(canvasId, value, maxValue = 10, alertThreshold = null) {
    console.log(`renderSingleGauge called for ${canvasId}: value=${value}, max=${maxValue}, threshold=${alertThreshold}`);
    
    try {
        // Définir les couleurs en fonction du seuil d'alerte
        let colors = { good: '#4CAF50', warning: '#FF9800', danger: '#F44336' };
        
        if (alertThreshold !== null && alertThreshold > 0) {
            // Ajuster les seuils de couleur en fonction du seuil d'alerte
            const warningThreshold = alertThreshold * 0.8;
            const dangerThreshold = alertThreshold;
            
            if (value >= dangerThreshold) {
                colors = { good: '#F44336', warning: '#F44336', danger: '#F44336' };
            } else if (value >= warningThreshold) {
                colors = { good: '#FF9800', warning: '#FF9800', danger: '#F44336' };
            }
        }
        
        const success = createGauge(canvasId, value, maxValue, '', colors);
        
        if (success) {
            console.log(`Gauge ${canvasId} rendered successfully`);
        } else {
            console.warn(`Failed to render gauge ${canvasId}`);
        }
        return success;
    } catch (error) {
        console.error(`Error rendering gauge ${canvasId}:`, error);
        return false;
    }
};

// MAINTENU pour la compatibilité - mais utilise maintenant les valeurs max par défaut
window.renderDashboardGauges = function(value1, value2, value3) {
    console.log(`renderDashboardGauges called with values: ${value1}, ${value2}, ${value3}`);
    
    try {
        let success = true;
        success &= window.renderSingleGauge('gauge1', value1, 10);
        success &= window.renderSingleGauge('gauge2', value2, 10);
        success &= window.renderSingleGauge('gauge3', value3, 10);
        
        if (success) {
            console.log('All gauges rendered successfully');
        } else {
            console.warn('Some gauges failed to render');
        }
        return success;
    } catch (error) {
        console.error('Error rendering gauges:', error);
        return false;
    }
};

window.dashboardFunctions = {
    createGauge: createGauge,
    renderSingleGauge: window.renderSingleGauge,
    renderGauges: window.renderDashboardGauges
};

// Fonction pour tester si les gauges sont accessibles
window.testGauges = function() {
    const gauge1 = document.getElementById('gauge1');
    const gauge2 = document.getElementById('gauge2');
    const gauge3 = document.getElementById('gauge3');
    
    console.log('Testing gauges...');
    console.log('Gauge1 exists:', !!gauge1);
    console.log('Gauge2 exists:', !!gauge2);
    console.log('Gauge3 exists:', !!gauge3);
    
    if (gauge1 && gauge2 && gauge3) {
        console.log('All gauges found, rendering test values...');
        return window.renderDashboardGauges(3.5, 7.2, 8.9);
    } else {
        console.warn('Some gauges not found');
        return false;
    }
};

// Fonction pour attendre que les éléments soient disponibles
window.waitForGauges = function(maxAttempts = 10, interval = 500) {
    let attempts = 0;
    
    const checkGauges = () => {
        attempts++;
        console.log(`Attempt ${attempts} to find gauges...`);
        
        if (window.testGauges()) {
            console.log('Gauges found and rendered!');
            return true;
        }
        
        if (attempts < maxAttempts) {
            setTimeout(checkGauges, interval);
        } else {
            console.error('Max attempts reached, gauges not found');
        }
        return false;
    };
    
    return checkGauges();
};

// Auto-test après chargement du DOM
document.addEventListener('DOMContentLoaded', function() {
    console.log('Dashboard.js loaded');
    
    // Essayer immédiatement
    setTimeout(() => {
        if (!window.testGauges()) {
            // Si ça ne marche pas, attendre et réessayer
            window.waitForGauges();
        }
    }, 100);
});

// Export pour que Blazor puisse appeler cette fonction
window.blazorDashboard = {
    renderSingleGauge: window.renderSingleGauge,
    renderGauges: window.renderDashboardGauges,
    testGauges: window.testGauges,
    waitForGauges: window.waitForGauges,
    scrollIndicators: window.scrollIndicators
};

// Fonction pour faire défiler la section des indicateurs
window.scrollIndicators = function(direction) {
    const container = document.querySelector('.indicators-section.scrollable');
    if (!container) return;
    
    const containerWidth = container.clientWidth;
    const scrollAmount = Math.min(containerWidth * 0.8, 300); // Défiler 80% de la largeur visible ou max 300px
    
    if (direction === 'left') {
        container.scrollBy({ left: -scrollAmount, behavior: 'smooth' });
    } else if (direction === 'right') {
        container.scrollBy({ left: scrollAmount, behavior: 'smooth' });
    }
};

// Fonction pour gérer la visibilité des boutons de défilement
window.updateScrollButtons = function() {
    const container = document.querySelector('.indicators-section.scrollable');
    const leftBtn = document.querySelector('.scroll-btn-side.scroll-left');
    const rightBtn = document.querySelector('.scroll-btn-side.scroll-right');
    
    if (!container || !leftBtn || !rightBtn) return;
    
    const scrollLeft = container.scrollLeft;
    const maxScroll = container.scrollWidth - container.clientWidth;
    
    // Désactiver le bouton gauche si on est au début
    if (scrollLeft <= 0) {
        leftBtn.style.opacity = '0.5';
        leftBtn.style.cursor = 'not-allowed';
    } else {
        leftBtn.style.opacity = '1';
        leftBtn.style.cursor = 'pointer';
    }
    
    // Désactiver le bouton droit si on est à la fin
    if (scrollLeft >= maxScroll - 1) {
        rightBtn.style.opacity = '0.5';
        rightBtn.style.cursor = 'not-allowed';
    } else {
        rightBtn.style.opacity = '1';
        rightBtn.style.cursor = 'pointer';
    }
};

// Écouter les événements de défilement pour mettre à jour les boutons
document.addEventListener('DOMContentLoaded', function() {
    const container = document.querySelector('.indicators-section.scrollable');
    if (container) {
        container.addEventListener('scroll', window.updateScrollButtons);
        // Initialiser l'état des boutons
        setTimeout(window.updateScrollButtons, 100);
    }
});

// Ajouter aux exports Blazor
window.blazorDashboard.scrollIndicators = window.scrollIndicators;
window.blazorDashboard.updateScrollButtons = window.updateScrollButtons;