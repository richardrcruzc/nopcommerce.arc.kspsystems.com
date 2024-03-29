﻿//------------------------------------------------------------------------------
// Contributor(s): mb 10/20/2010. 
//------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;

namespace Nop.Plugin.Shipping.UPS.Domain
{
    /// <summary>
    /// Class for UPS services
    /// </summary>
    public static class UPSServices
    {
        #region Fields

        /// <summary>
        /// UPS Service names
        /// </summary>
        private static readonly Dictionary<string, string> _services = new Dictionary<string, string>
        {
            {"Next Day Air", "01"},
            {"2nd Day Air", "02"},
            {"Ground", "03"},
            {"Worldwide Express", "07"},
            {"Worldwide Expedited", "08"},
            {"Canada Standard", "11"},
            {"3 Day Select", "12"},
            {"Next Day Air Saver", "13"},
            {"Next Day Air Early A.M.", "14"},
            {"Worldwide Express Plus", "54"},
            {"2nd Day Air A.M.", "59"},
            {"Saver", "65"},
            {"Today Standard", "82"}, //82-86, for Polish Domestic Shipments
            {"Today Dedicated Courier", "83"},
            {"Today Express", "85"},
            {"Today Express Saver", "86"},
            {"Saturday Delivery", "sa"},
        };


           //{"Next Day Air", "01"},
           // {"2nd Day Air", "02"},
           // {"UPS Ground", "03"},
           // {"UPS Worldwide Express", "07"},
           // {"UPS Worldwide Expedited", "08"},
           // {"UPS Canada Standard", "11"},
           // {"3 Day Select", "12"},
           // {"Next Day Air Saver", "13"},
           // {"UPS Next Day Air Early A.M.", "14"},
           // {"UPS Worldwide Express Plus", "54"},
           // {"UPS 2nd Day Air A.M.", "59"},
           // {"UPS Saver", "65"},
           // {"UPS Today Standard", "82"}, //82-86, for Polish Domestic Shipments
           // {"UPS Today Dedicated Courier", "83"},
           // {"UPS Today Express", "85"},
           // {"UPS Today Express Saver", "86"},
           // {"Saturday Delivery", "sa"},

        #endregion
    
        #region Utilities

        /// <summary>
        /// Gets the Service ID for a service
        /// </summary>
        /// <param name="service">service name</param>
        /// <returns>service id or empty string if not found</returns>
        public static string GetServiceId(string service)
        {
            var serviceId = "";
            if (string.IsNullOrEmpty(service))
                return serviceId;

            if (_services.ContainsKey(service))
                serviceId = _services[service];

            return serviceId;
        }

        #endregion

        #region Properties

        /// <summary>
        /// UPS services string names
        /// </summary>
        public static string[] Services
        {
            get
            {
                return _services.Keys.Select(x => x).ToArray();
            }
        }

        #endregion

    }
}
