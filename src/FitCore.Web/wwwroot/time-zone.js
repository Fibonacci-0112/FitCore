// Fills inputs marked data-time-zone with the browser's IANA time zone, so sign-up can store the member's local
// calendar (BRD §6.1). Runs on full page loads and after Blazor's enhanced navigation.
(() => {
  const fill = () => {
    const zone = Intl.DateTimeFormat().resolvedOptions().timeZone;
    for (const input of document.querySelectorAll('input[data-time-zone]')) {
      input.value = zone;
    }
  };

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', fill);
  } else {
    fill();
  }

  window.Blazor?.addEventListener('enhancedload', fill);
})();
