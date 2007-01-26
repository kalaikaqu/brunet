using System;
using System.IO;
using System.Collections;
using System.Net;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace Ipop {
  public class IPAddressesWindows : IEnumerable {
    protected ArrayList _ints;

    public IPAddressesWindows(string[] interfaces) {
      _ints = new ArrayList(interfaces);
    }

    /**
     * Get all the IPAddresses
     */
    public IPAddressesWindows() {
      _ints = null;
    }

    /**
     * This is for IEnumerable/foreach support
     * 
     */
    public IEnumerator GetEnumerator() {
      IList all_interfaces = GetOutput();
      foreach(Hashtable ht in all_interfaces) {
        if( ht.ContainsKey("interface") && ht.ContainsKey("inet addr") ) {
          string iface = (string)ht["interface"];
          if( _ints == null ) {
            yield return IPAddress.Parse( (string)ht["inet addr"] );
          }
          else if( _ints.Contains(iface) ) {
            yield return IPAddress.Parse( (string)ht["inet addr"] );
          }
        }
      }
    }

    public IList GetOutput() {
      ProcessStartInfo cmd = new ProcessStartInfo("c:\\WINDOWS\\system32\\ipconfig");
      cmd.Arguments = "/all";
      cmd.RedirectStandardOutput = true;
      cmd.UseShellExecute = false;
      Process p = Process.Start(cmd);

      string line = p.StandardOutput.ReadLine();
      //string this_if = null;
      Regex if_line = new Regex(@"Ethernet adapter ([^:]+)");

      Hashtable keys = new Hashtable();
      keys["inet addr"] = new Regex(@"IP Address. . . . . . . . . . . . : (\S+)");
      keys["Bcast"] = new Regex(@"DOES NOT EXIST(\S+)");
      keys["Mask"] = new Regex(@"Subnet Mask . . . . . . . . . . . : (\S+)");
      keys["HWaddr"] = new Regex(@"Physical Address. . . . . . . . . : ([0-9A-F\-]+)");
      keys["MTU"] = new Regex(@"DOES NOT EXIST([0-9]+)");

      ArrayList result = new ArrayList();
      Hashtable entry = null;
      while( line != null ) {
        //Get the interface:
        Match m = if_line.Match(line);
        if( m.Success ) {
        /*
         * This is a new Interface, add the old one to the list:
         */
          if( entry != null ) {
            result.Add(entry);
          }
          entry = new Hashtable();
          //System.Console.WriteLine(line);
          Group g = m.Groups[1];
          CaptureCollection cc = g.Captures;
          //System.Console.WriteLine(cc[0]);
          entry["interface"] = cc[0].ToString();
        }
        IDictionaryEnumerator key_en = keys.GetEnumerator();
        while(key_en.MoveNext() ) {
          AddIfMatch((Regex)key_en.Value, line, entry, (string)key_en.Key);
        }
        line = p.StandardOutput.ReadLine();
      }
      if( entry != null ) {
        result.Add(entry);
      }
      return result;
    }

    protected bool AddIfMatch(Regex re, string line, Hashtable ht, string
key) {
      Match m = re.Match(line);
      if( m.Success ) {
        //System.Console.WriteLine(line);
        Group g = m.Groups[1];
        CaptureCollection cc = g.Captures;
        //System.Console.WriteLine(cc[0]);
        ht[key] = cc[0].ToString();
        return true;
      }
      return false;
    }

    protected void Print(IList l) {
      foreach(Hashtable ht in l) {
        IDictionaryEnumerator en = ht.GetEnumerator();
        while(en.MoveNext()) {
          System.Console.WriteLine("{0}: {1}", en.Key, en.Value);
        }
        System.Console.WriteLine();
      }
    }
  }
}
