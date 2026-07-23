namespace Scanner
{
    // Parámetros de lanzamiento de la ScannerScene, seteados por el menú principal
    // ANTES de cargar la escena (estático: sobrevive el LoadScene).
    public static class ScannerLaunchParams
    {
        // Nombre del escaneo guardado a abrir para editar, o null para uno nuevo.
        public static string EditScanName;

        // Lee y limpia el parámetro (se consume una sola vez por carga de escena).
        public static string ConsumeEditScanName()
        {
            var n = EditScanName;
            EditScanName = null;
            return n;
        }
    }
}
