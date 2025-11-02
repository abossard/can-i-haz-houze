// Preloader script to improve perceived performance
// This script runs before Blazor starts to provide immediate feedback

(function() {
    'use strict';
    
    // Show a friendly loading message while Blazor initializes
    window.addEventListener('DOMContentLoaded', function() {
        console.log('CanIHazHouze: Page loaded, initializing Blazor...');
    });

    // Cache strategy: Preload common resources
    if ('serviceWorker' in navigator) {
        // Future enhancement: Register a service worker for offline support
        console.log('CanIHazHouze: Service Worker support detected');
    }
})();
