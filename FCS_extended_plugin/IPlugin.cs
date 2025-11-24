using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace FCS_extended
{
	public interface IPlugin
	{
		int Init(Assembly assembly);
	}
}
