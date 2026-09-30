using System;
using System.Collections.Generic;
using System.Linq;
using PrintVect.Core.Config;

namespace PrintVect.Core.Host
{
    /// <summary>
    /// Finds the shared printer a request means. Clients send the id; people typing into
    /// pvct-send may use the friendly name or the Windows printer name instead.
    /// </summary>
    public static class SharedPrinterResolver
    {
        public static SharedPrinter Find(IEnumerable<SharedPrinter> printers, string key)
        {
            if (printers == null || string.IsNullOrWhiteSpace(key))
            {
                return null;
            }
            key = key.Trim();
            List<SharedPrinter> list = printers.Where(p => p != null).ToList();

            return list.FirstOrDefault(p => string.Equals(p.Id, key, StringComparison.Ordinal))
                   ?? list.FirstOrDefault(p => string.Equals(p.FriendlyName, key, StringComparison.OrdinalIgnoreCase))
                   ?? list.FirstOrDefault(p => string.Equals(p.LocalName, key, StringComparison.OrdinalIgnoreCase));
        }
    }
}
