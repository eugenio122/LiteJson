using System;
using System.Collections.Generic;
using LiteJson.Models;

namespace LiteJson.Adapters
{
    public class SapGuiAdapter : IUIAdapter
    {
        public string EngineName => "SAP_GUI_Scripting";

        public bool CanHandle(IntPtr hwnd)
        {
            // TODO: Verificar se a classe da janela pertence ao SAP Logon ("SAP_FRONTEND_SESSION")
            return false;
        }

        public ElementData ExtractElementTree(IntPtr hwnd, int x, int y, List<string> qualityFlagsOut)
        {
            var data = new ElementData();

            try
            {
                // TODO: Aceder à Running Object Table (ROT) do Windows para pegar o "SAPGUI"
                // Navegar pela árvore do SAP.FindById usando hit-testing interno do SAP

                data.Role = "SapNode_Pending";
                data.FrameworkId = "SAP_GUI";
                data.SapId = "wnd[0]/usr/pending_id";

                qualityFlagsOut.Add("SAP_EXTRACTION_PENDING");
            }
            catch (Exception)
            {
                qualityFlagsOut.Add("SAP_EXTRACTION_ERROR");
            }

            return data;
        }
    }
}