using System;
using System.Threading.Tasks;

namespace NOVAxis.Utilities
{
    public delegate Task AsyncEventHandler<in TEventArgs>(object sender, TEventArgs eventArgs);
    public delegate Task AsyncEventHandler(object sender, EventArgs eventArgs);
}
