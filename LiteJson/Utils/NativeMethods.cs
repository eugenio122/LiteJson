using System;
using System.Runtime.InteropServices;
using System.Text;

namespace LiteJson.Utils
{
    /// <summary>
    /// Encapsula chamadas a APIs nativas do Windows (P/Invoke).
    /// </summary>
    internal static class NativeMethods
    {
        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        public static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        // Espaço reservado para outras APIs como GetWindowRect, ClientToScreen, etc.
    }
}