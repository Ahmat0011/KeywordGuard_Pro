using System;

namespace KeywordGuard.Pro.Security;

public static class UrlHelper
{
    /// <summary>
    /// Versucht, eine Domain aus einer URL oder einem Keyword zu extrahieren.
    /// Gibt null zurück, wenn es sich nicht um eine Domain/URL handelt.
    /// </summary>
    public static string? ExtractDomain(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;
        
        string temp = input.Trim().ToLower();
        
        // Protokolle entfernen
        if (temp.StartsWith("http://"))
            temp = temp.Substring(7);
        else if (temp.StartsWith("https://"))
            temp = temp.Substring(8);
            
        // www. entfernen
        if (temp.StartsWith("www."))
            temp = temp.Substring(4);
            
        // Pfad, Query-Parameter und Port entfernen
        int slashIndex = temp.IndexOf('/');
        if (slashIndex >= 0)
            temp = temp.Substring(0, slashIndex);
            
        int colonIndex = temp.IndexOf(':');
        if (colonIndex >= 0)
            temp = temp.Substring(0, colonIndex);
            
        temp = temp.Trim();
        
        // Eine valide Domain muss mindestens einen Punkt enthalten, keine Leerzeichen,
        // und die TLD (letzter Teil) muss mindestens 2 Zeichen lang sein.
        if (temp.Contains('.') && !temp.Contains(' '))
        {
            int lastDot = temp.LastIndexOf('.');
            if (lastDot > 0 && temp.Length - lastDot - 1 >= 2)
            {
                return temp;
            }
        }
        
        return null;
    }
}
