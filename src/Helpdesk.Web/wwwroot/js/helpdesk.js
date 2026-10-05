// Funciones mínimas de JavaScript (archivo propio: la CSP no permite scripts en línea).
window.helpdesk = {
  // Descarga un archivo recibido por la API autenticada (adjuntos, CSV).
  downloadFile: function (fileName, contentType, base64) {
    const bytes = Uint8Array.from(atob(base64), c => c.charCodeAt(0));
    const url = URL.createObjectURL(new Blob([bytes], { type: contentType }));
    const link = document.createElement("a");
    link.href = url;
    link.download = fileName;
    link.rel = "noopener";
    document.body.appendChild(link);
    link.click();
    link.remove();
    setTimeout(() => URL.revokeObjectURL(url), 1000);
  },
  // El refresh token vive en sessionStorage: se borra al cerrar la pestaña.
  session: {
    get: key => sessionStorage.getItem(key),
    set: (key, value) => sessionStorage.setItem(key, value),
    remove: key => sessionStorage.removeItem(key)
  }
};

document.addEventListener("click", event => {
  if (event.target && event.target.classList && event.target.classList.contains("dismiss")) {
    document.getElementById("blazor-error-ui").style.display = "none";
  }
});
