using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BrewDogTaps
{
    public class AccessTokenRequest
    {
        public String id { get; set; }
    }

    public class AccessTokenResponse
    {
        public String token { get; set; }
        public String error { get; set; }

        public Boolean IsError()
        {
            return !String.IsNullOrWhiteSpace(error);
        }
    }

    public class Tap
    {
        public string name { get; set; }
        public string abv { get; set; }
        public string description { get; set; }
        public string code { get; set; }
    }

    public class Data
    {
        public string address1 { get; set; }
        public string address2 { get; set; }
        public string address3 { get; set; }
        public string city { get; set; }
        public string email { get; set; }
        public string facebook { get; set; }
        public string geo { get; set; }
        public string hours { get; set; }
        public string mobile { get; set; }
        public string phone { get; set; }
        public string postcode { get; set; }
        public string twitter { get; set; }
        public List<Tap> tap { get; set; }
    }

    public class Bar
    {
        public int id { get; set; }
        public string time { get; set; }
        public string name { get; set; }
        public string photo { get; set; }
        public Data data { get; set; }
    }

    public class BarDataResponse
    {
        public int version { get; set; }
        public List<Bar> bars { get; set; }
    }
}
