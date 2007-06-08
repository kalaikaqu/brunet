using System;
using System.Net;
using System.Text;
using System.Threading;
using System.Collections;
using System.Security.Cryptography;

using Brunet;
using Brunet.Dht;

namespace Ipop {
/**
* This class implements a route miss handler in case we cannot find
* a virtual Ip -> brunet Id mapping inside our translation table. 
* This lacks a way to expire entries, the old method would only expire entries
* if the object had been pushed out of the stack
**/
  public class Routes {
    protected object _res_sync = new object(), _queue_sync = new object();
    private Hashtable _results = new Hashtable(), _queued = new Hashtable();
    private FDht _dht = null;
    private string _ipop_namespace;

    public Routes(FDht dht, string ipop_namespace) {
      _dht = dht;
      _ipop_namespace = ipop_namespace;
    }

    public Address GetAddress(IPAddress ip) {
      lock(_res_sync) {
        byte[] buf =  (byte[]) _result[ip];
        if(null != buf) {
          return new AHAddress( MemBlock.Reference(buf) );
        }
      }
      return null;
    }

    public void RouteMiss(IPAddress ip) {
      lock(_queue_sync) {
        if (!_queued.Contains(ip)) {
          /*
          * If we were already looking up this IPAddress, there
          * would be a table entry, since there is not, start a
          * new lookup
          */
          _queued[ip] = true;
          ThreadPool.QueueUserWorkItem(new WaitCallback(this.RouteMiss), ip);
        }
      }
    }

    public void RouteMiss(object oip) {
      IPAddress ip = (IPAddress) oip;
      string key = "dhcp:ipop_namespace:" + _ipop_namespace + ":ip:" + ip.ToString();
      DhtOp dhtOp = new DhtOp(_dht);
      DhtGetResult [] dgr = null;
      try {
        dgr = dhtOp.Get(key);
	lock( _res_sync ) {
          _results[ip] = dgr[0].value;
	}
      }
      catch(Exception x) { System.Console.Error.WriteLine("In RouteMiss({1}): {0}", x, ip); }

      lock(_queue_sync) {
        _queued.Remove(ip);
      }
    }
  }
}
