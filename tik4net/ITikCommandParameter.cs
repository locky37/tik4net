﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace tik4net
{
    public interface ITikCommandParameter
    {
        string Name { get; set; }
        string Value { get; set; }
    }
}
