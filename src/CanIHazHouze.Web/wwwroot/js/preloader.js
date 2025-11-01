// Preloader script to improve perceived performance
// This script runs before Blazor starts to provide immediate feedback

(function() {
    'use strict';
    
    // Show a friendly loading message while Blazor initializes
    window.addEventListener('DOMContentLoaded', function() {
        console.log('CanIHazHouze: Page loaded, initializing Blazor...');
    });

    // Add connection state monitoring
    if (typeof Blazor !== 'undefined') {
        Blazor.defaultReconnectionHandler._reconnectCallback = async function(d) {
            console.log('Blazor: Attempting to reconnect...');
            return true; // Allow default reconnection
        };
    }

    // Cache strategy: Preload common resources
    if ('serviceWorker' in navigator) {
        // Future enhancement: Register a service worker for offline support
        console.log('CanIHazHouze: Service Worker support detected');
    }
})();
