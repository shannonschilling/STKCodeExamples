//=====================================================//
//  Copyright 2005, Analytical Graphics, Inc.          //
//=====================================================//
using Microsoft.Win32;
using System;
using System.Collections;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

using AGI.Attr;
using AGI.Plugin;
using AGI.Access.Constraint.Plugin;
using AGI.VectorGeometryTool.Plugin;
using System.IO;
using System.Collections.Generic;
using System.Linq;

namespace AGI.Access.Constraint.Plugin.CSharp.ReadFromFile
{
	/// <summary>
	/// Constraint Plugin component ReadFromFile
	/// Original Author:     Reicher
	/// Company:    Analytical Graphics, Inc.
	/// Copyright:  None.  Modify and distribute at will
	///
	/// Description:
	/// 
	/// This constraint is registered only for Facilities/Targets when doing
	/// Access to a Sensor.
	/// The assumption is that the user provides lamda, Q, and D within the script and
	/// STK does the rest.  GSD is returned in meters.  Then we pass it into the NIIRS
	/// equation and return the NIIRS value.
	/// </summary>

	[Guid("A52A0602-BA50-42fc-8893-45DC81F2DE7D")]
	[ProgId("AGI.Access.Constraint.Plugin.CSharp.ReadFromFile")]
	// NOTE: Specify the ClassInterfaceType.None enumeration, so the custom COM Interface 
	// you created, i.e. IParameters, is used instead of an autogenerated COM Interface.
	[ClassInterface(ClassInterfaceType.None)]
	public class FileReader : 
		IParameters,
		IAgAccessConstraintPlugin,
		IAgUtPluginConfig
	{
		#region Data Members
		private string	m_DisplayName = "ReadFromFile";

		private IAgUtPluginSite  m_Site;
		private object			 m_Scope;	

		private static string s_externalFilePath;
		private static List<ExternalValue> s_externalValues;
		private static Dictionary<string,double> s_cataloguedValues;
		// debug

		private bool			m_DebugMode;
		private int				m_MsgCntr;
		private int				m_MsgInterval;

		#endregion
		
		public FileReader()
		{
			// defaults

			m_Site = null;
			m_Scope = null;


			m_DebugMode = false;	// NOTE: if true, will output a msg when
									// entering events other than Evaluate().
									//
									// DON'T set to true when using constraint as a
									// Figure of Merit,because PreCompute() and PostCompute()
									// are called once per animation step, which will cause
									// lots of messages to be written to the Message Viewer.
			m_MsgCntr = 0;
			m_MsgInterval = 100;
		}
		
		#region IAgAccessConstraintPlugin implementation

		public string DisplayName
		{
			get
			{
				return m_DisplayName;
			}
		}

		public void Register( AgAccessConstraintPluginResultRegister Result )
		{
			Result.BaseObjectType = AgEAccessConstraintObjectType.eFacility;
			Result.BaseDependency = (int)AgEAccessConstraintDependencyFlags.eDependencyRelativePosVel;
			Result.Dimension = "Unitless";	
			Result.MinValue = 0.0;
		
			Result.TargetDependency = (int)AgEAccessConstraintDependencyFlags.eDependencyNone;
			Result.AddTarget(AgEAccessConstraintObjectType.eSensor);
			Result.Register();

			Result.Message(AGI.Plugin.AgEUtLogMsgType.eUtLogMsgInfo, 
						m_DisplayName+": Register(Facility to Sensor)");
		
			Result.BaseObjectType = AgEAccessConstraintObjectType.eTarget;
			Result.Register();

			Result.Message(AGI.Plugin.AgEUtLogMsgType.eUtLogMsgInfo, 
				m_DisplayName+": Register(Target to Sensor)");
		}

		public bool Init( IAgUtPluginSite site )
		{
			m_Site = site;
			
			if( m_Site != null && m_DebugMode)
			{
				Message( AgEUtLogMsgType.eUtLogMsgInfo, m_DisplayName+": Init()" );
			}

			return true;
		}

		public bool PreCompute( AgAccessConstraintPluginResultPreCompute Result )
		{

			if( m_Site != null && m_DebugMode)
			{
				Message( AgEUtLogMsgType.eUtLogMsgInfo, m_DisplayName+": PreCompute()" );
			}

			return true;
		}

	
		public bool Evaluate( 
			AgAccessConstraintPluginResultEval Result, 
			AgAccessConstraintPluginObjectData baseObj, 
			AgAccessConstraintPluginObjectData targetObj )
		{
			if(Result != null)
			{
				Result.Value = 0.0;
			
				if( baseObj != null)
				{
					// Get teh grid point location
					double lat = 0, lon = 0, alt = 0;
					baseObj.LatLonAlt(ref lat, ref lon, ref alt);
					
					// convert from Rad to Deg
					lat = lat * 180.0 / Math.PI;
					lon = lon * 180.0 / Math.PI;

					//Capture as a string for the lookuptable
					string ID = lat.ToString("f3") + "," + lon.ToString("f3");

					// Check to see if this location is already in teh lookup table
					if (s_cataloguedValues.ContainsKey(ID))
					{
						// if it is, get the value and return
						Result.Value = s_cataloguedValues[ID];
						return true;
					}

					// if we havent found the closest point yet, check the external data

					var topFour = s_externalValues.AsParallel().Where(v=> v.Value != -9999).OrderBy(v => Math.Sqrt(Math.Pow(lat - v.Latitude, 2) + Math.Pow(lon - v.Longitude, 2))).Take(4);
					var sum = topFour.Sum(v => v.Value / Math.Sqrt(Math.Pow(lat - v.Latitude, 2) + Math.Pow(lon - v.Longitude, 2)));
					var denominator = topFour.Sum(v => 1 / Math.Sqrt(Math.Pow(lat - v.Latitude, 2) + Math.Pow(lon - v.Longitude, 2)));
					var weightedGridValue = sum / denominator;
					// Return the value of the closest point in the external data set
					Result.Value = weightedGridValue;
					// store the value of the clsoest point in a lookup for quicker retrieval next time
					s_cataloguedValues.Add(ID, weightedGridValue);

					
				}
			}

			return true;
		}
		
		public bool PostCompute(AgAccessConstraintPluginResultPostCompute Result)
		{
			if( m_Site != null && m_DebugMode)
			{
				Message( AgEUtLogMsgType.eUtLogMsgInfo, m_DisplayName+": PostCompute()" );
			}
			return true;
		}

		public void Free()
		{
			if( m_Site != null && m_DebugMode)
			{
				Message( AgEUtLogMsgType.eUtLogMsgInfo, m_DisplayName+": Free()" );
			}

			m_Site = null;
		}
		
		#endregion

		
		#region IAgUtPluginConfig Interface Implementation
		public object GetPluginConfig( AgAttrBuilder aab )
		{
			try
			{
				if( this.m_Scope == null )
				{
					this.m_Scope = aab.NewScope();

					
					aab.AddStringDispatchProperty ( this.m_Scope, "ExternalFilePath",
						"ExternalFilePath",
						"ExternalFilePath", 
						(int)AgEAttrAddFlags.eAddFlagNone );
				
					//===========================
					// Debug attributes
					//===========================
					aab.AddBoolDispatchProperty( this.m_Scope, "DebugMode", 
						"Turn debug messages on or off", 
						"DebugMode", 
						(int)AgEAttrAddFlags.eAddFlagNone );
				
					aab.AddIntDispatchProperty( this.m_Scope, "MessageInterval", 
						"The interval at which to send messages during propagation in Debug mode", 
						"MsgInterval", 
						(int)AgEAttrAddFlags.eAddFlagNone );
				}
			}
			finally
			{

			}
	
			return this.m_Scope;
		}

		public void VerifyPluginConfig( AgUtPluginConfigVerifyResult apcvr )
		{
			bool	result	= true;
			string	message = "Ok";
								
			apcvr.Result	= result;
			apcvr.Message	= message;
		}

#endregion

		#region IParameters Interface Implementation

		
		public string ExternalFilePath
		{
			get
			{
				return s_externalFilePath;
			}
			set
			{
				// check to see if it's necessary to read the contents of the external file
				if (string.IsNullOrEmpty(value) || value.CompareTo(s_externalFilePath) == 0)
				{
					return;
				}

				// initialize variables
				s_externalFilePath = value ;
				s_externalValues = new List<ExternalValue>();
				s_cataloguedValues = new Dictionary<string, double>();

				// read the contents of the external file
				string[] lines = File.ReadAllLines(s_externalFilePath);


				// format is assumed to be a gridded CSV
				// first row is longitude values
				// first column is latitude values
				// values fill in the grid...
				// -------Example File--------
				// empty,Lon1,Lon2,Lon3,LonN				
				// Lat1,Value1_1,Value1_2,Value1_3
				// Lat2,Value2_1,Value2_2,Value2_3
				// Lat3,Value3_1,Value3_2,Value3_3

				// parse the external data
				var lonArray = lines[0].Split(',').Skip(1).Select(v => double.Parse(v));
				foreach (var line in lines.Skip(1))
				{
					var splitLine = line.Split(',');
					double lat = double.Parse(splitLine[0]);
					var i = 1;
					foreach (var lon in lonArray)
					{
						ExternalValue externalValue = new ExternalValue()
						{
							Latitude = lat,
							Longitude = lon,
							Value = double.Parse(splitLine[i])
						};

						s_externalValues.Add(externalValue);
						++i;
					}

				}
			}
		}

		public bool DebugMode
		{
			get
			{
				return this.m_DebugMode;
			}
			set
			{
				this.m_DebugMode = value;
			}
		}

		public int MsgInterval
		{
			get
			{
				return this.m_MsgInterval;
			}
			set
			{
				this.m_MsgInterval = value;
			}
		}

		
		#endregion

		#region Messaging Code

		private void Message (AgEUtLogMsgType severity, String msgStr)
		{
			if(  this.m_Site != null )
			{
				this.m_Site.Message( severity, msgStr);
			}
		}

		private void DebugMessage(String msgStr)
		{
			if(m_DebugMode)
			{
				if(m_MsgCntr % m_MsgInterval == 0)
				{
					Message(AgEUtLogMsgType.eUtLogMsgDebug, msgStr);
				}
			}
		}

		#endregion

        #region Registration functions
        /// <summary>
        /// Called when the assembly is registered for use from COM.
        /// </summary>
        /// <param name="t">The type being exposed to COM.</param>
        [ComRegisterFunction]
        [ComVisible(false)]
        public static void RegisterFunction(Type t)
        {
            RemoveOtherVersions(t);
        }

        /// <summary>
        /// Called when the assembly is unregistered for use from COM.
        /// </summary>
        /// <param name="t">The type exposed to COM.</param>
        [ComUnregisterFunctionAttribute]
        [ComVisible(false)]
        public static void UnregisterFunction(Type t)
        {
            // Do nothing.
        }

        /// <summary>
        /// Called when the assembly is registered for use from COM.
        /// Eliminates the other versions present in the registry for
        /// this type.
        /// </summary>
        /// <param name="t">The type being exposed to COM.</param>
        public static void RemoveOtherVersions(Type t)
        {
            try
            {
                using (RegistryKey clsidKey = Registry.ClassesRoot.OpenSubKey("CLSID"))
                {
                    StringBuilder guidString = new StringBuilder("{");
                    guidString.Append(t.GUID.ToString());
                    guidString.Append("}");
                    using (RegistryKey guidKey = clsidKey.OpenSubKey(guidString.ToString()))
                    {
                        if (guidKey != null)
                        {
                            using (RegistryKey inproc32Key = guidKey.OpenSubKey("InprocServer32", true))
                            {
                                if (inproc32Key != null)
                                {
                                    string currentVersion = t.Assembly.GetName().Version.ToString();
                                    string[] subKeyNames = inproc32Key.GetSubKeyNames();
                                    if (subKeyNames.Length > 1)
                                    {
                                        foreach (string subKeyName in subKeyNames)
                                        {
                                            if (subKeyName != currentVersion)
                                            {
                                                inproc32Key.DeleteSubKey(subKeyName);
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch
            {
                // Ignore all exceptions...
            }
        }
        #endregion
	}
}
//=====================================================//
//  Copyright 2006, Analytical Graphics, Inc.          //
//=====================================================//