﻿using GodaddyWrapper.Attributes;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;

namespace GodaddyWrapper.Requests
{
    public class VerifyRegistrantEmail
    {
        [Required]
        public string domain { get; set; }
    }
}
