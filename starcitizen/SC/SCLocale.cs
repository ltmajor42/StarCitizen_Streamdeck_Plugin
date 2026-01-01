using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace starcitizen.SC
{
  class SCLocale : Dictionary<string,string>
  {

    public string Language { get; set; } // easier for debuging if knowing the expected language

    public  SCLocale(string lang )
    {
      Language = lang;
    }
  }
}
