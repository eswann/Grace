﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Grace.Selenium
{
    public interface ISeleniumSession : IDictionary<string, object>
    {
        
    }

    public class SeleniumSession : Dictionary<string,object>, ISeleniumSession
    {

    }
}
