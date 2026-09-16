// Show Bootstrap toast notification for new reports
function showReportToast(reportCount, timestamp) {
    // Update toast content
    document.getElementById('reportCount').textContent = reportCount;
    
    // Get or create Bootstrap toast instance
    var toastElement = document.getElementById('reportToast');
    if (toastElement) {
        var toast = new bootstrap.Toast(toastElement, {
            autohide: true,
            delay: 5000 // Show for 5 seconds
        });
        
        // Show the toast
        toast.show();
        
        // Optional: Play a sound
        playNotificationSound();
    }
}

// Optional: Play notification sound
function playNotificationSound() {
    try {
        var audio = new Audio('/sounds/notification.mp3');
        audio.volume = 0.3;
        audio.play().catch(e => console.log('Audio play failed:', e));
    } catch (e) {
        console.log('Audio not available:', e);
    }
}

// Optional: Browser notification (requires user permission)
function showBrowserNotification(reportCount) {
    if ('Notification' in window && Notification.permission === 'granted') {
        new Notification('InfluxDB - New Reports', {
            body: `${reportCount} new report(s) received`,
            icon: '/favicon.ico',
            badge: '/favicon.ico'
        });
    }
}

// Request notification permission
function requestNotificationPermission() {
    if ('Notification' in window && Notification.permission === 'default') {
        Notification.requestPermission();
    }
}