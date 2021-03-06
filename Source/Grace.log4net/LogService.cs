﻿using System;
using Grace.Logging;
using l4n = log4net;

namespace Grace.log4net
{
	/// <summary>
	/// Log service that wraps log4net LogManager
	/// </summary>
	public class LogService : ILogService
	{

		/// <summary>
		/// Get a log instance based on type
		/// </summary>
		/// <param name="type">type of logger to get</param>
		/// <returns>
		/// ILog instance
		/// </returns>
		public ILog GetLogger(Type type)
		{
			l4n.ILog log = l4n.LogManager.GetLogger(type);

			return new LogWrapper(log);
		}

		/// <summary>
		/// Get a log instance by name
		/// </summary>
		/// <param name="name">logger name to get</param>
		/// <returns>
		/// ILog instance
		/// </returns>
		public ILog GetLogger(string name)
		{
			l4n.ILog log = l4n.LogManager.GetLogger(name);

			return new LogWrapper(log);
		}
	}
}