// 
// ISelectFileDialog.cs
//  
// Author:
//       Lluis Sanchez Gual <lluis@novell.com>
// 
// Copyright (c) 2010 Novell, Inc (http://www.novell.com)
// 
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
// THE SOFTWARE.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GLib;
using Gtk;
using MonoDevelop.Core;
using MonoDevelop.Ide;

namespace MonoDevelop.Components.Extensions
{
	/// <summary>
	/// This interface can be implemented to provide a custom implementation
	/// for the SelectFileDialog dialog.
	/// </summary>
	public interface ISelectFileDialogHandler : IDialogHandler<SelectFileDialogData>
	{
	}
	
	/// <summary>
	/// Data for the ISelectFileDialogHandler implementation
	/// </summary>
	public class SelectFileDialogData: PlatformDialogData
	{
		public SelectFileDialogData () {
			FilterSet = new FileFilterSet ();
		}
		internal FileFilterSet FilterSet { get; set; } 
		public FileChooserAction Action { get; set; }
		public IList<SelectFileDialogFilter> Filters => FilterSet.Filters;
	    public FilePath CurrentFolder { get; set; }
		public bool SelectMultiple { get; set; }
		public FilePath[] SelectedFiles { get; set; }
		public string InitialFileName { get; set; }
		public SelectFileDialogFilter DefaultFilter {
			get { return FilterSet.DefaultFilter; }
			set { FilterSet.DefaultFilter = value; }
		}
		public bool ShowHidden { get; set; }
	}	
			
	/// <summary>
	/// Filter option to be displayed in file selector dialogs.
	/// </summary>
	public class SelectFileDialogFilter
	{
		public SelectFileDialogFilter (string name, params string[] patterns)
		{
			Name = name;
			Patterns = patterns;
		}
		
		public SelectFileDialogFilter (string name, IList<string> patterns, IList<string> mimetypes)
		{
			Name = name;
			Patterns = patterns;
			MimeTypes = mimetypes;
		}
		
		/// <summary>Label for the filter</summary>
		public string Name { get; }
		
		/// <summary>Filename glob patterns permitted by this filter</summary>
		public IList<string> Patterns { get; }
		
		/// <summary>MIME types permitted by this filter</summary>
		public IList<string> MimeTypes { get; }
		
		public static SelectFileDialogFilter AllFiles => new SelectFileDialogFilter ( ("All Files"), "*");
	}
	
	/// <summary>
	/// Generic class to be used to implement file selectors.
	/// The T type argument is the type of the handler.
	/// The U type is the type of the data parameter (must subclass SelectFileDialogData)
	/// </summary>
	public abstract class SelectFileDialog<T>: PlatformDialog<T> where T:SelectFileDialogData, new()
	{
		/// <summary>
		/// Action to perform with the file dialog.
		/// </summary>
		public FileChooserAction Action {
			get { return data.Action; }
			set { data.Action = value; }
		}
		
		/// <summary>
		/// Folder to show by default.
		/// </summary>
		public FilePath CurrentFolder {
			get { return data.CurrentFolder; }
			set { data.CurrentFolder = value; }
		}
		
		/// <summary>
		/// Set to True to allow multiple selection.
		/// </summary>
		public bool SelectMultiple {
			get { return data.SelectMultiple; }
			set { data.SelectMultiple = value; }
		}
		
		/// <summary>
		/// List of selected files (or folders).
		/// </summary>
		public FilePath[] SelectedFiles => data.SelectedFiles;

	    /// <summary>
		/// Selected file (or folder) when using single selection mode.
		/// </summary>
		public FilePath SelectedFile => data.SelectedFiles.Length > 0 ? data.SelectedFiles [0] : null;

	    /// <summary>
		/// File name to show by default.
		/// </summary>
		public string InitialFileName {
			get { return data.InitialFileName; }
			set { data.InitialFileName = value; }
		}
		
		/// <summary>
		/// File filters that allow the user to choose the kinds of files the dialog displays.
		/// </summary>
		public IList<SelectFileDialogFilter> Filters => data.Filters;

	    /// <summary>
		/// The default file filter. If there is only one, the user will not have a choice of file types.
		/// </summary>
		public SelectFileDialogFilter DefaultFilter {
			get { return data.DefaultFilter; }
			set { data.DefaultFilter = value; }
		}

		/// <summary>
		/// Gets or sets whether the file dialog will show hidden files and folders.
		/// show hidden.
		/// </summary>
		/// <value><c>true</c> if hidden files are shown; otherwise, <c>false</c>.</value>
		public bool ShowHidden {
			get { return data.ShowHidden; }
			set { data.ShowHidden = value; }
		}
		
		#region File filter utilities
		
		internal void SetFilters (FileFilterSet filters)
		{
			data.FilterSet = filters;
		}
				
		public SelectFileDialogFilter AddFilter (string label, params string[] patterns)
		{
			return data.FilterSet.AddFilter (label, patterns);
		}
		
		public SelectFileDialogFilter AddFilter (SelectFileDialogFilter filter)
		{
			return data.FilterSet.AddFilter (filter);
		}
		
		public SelectFileDialogFilter AddAllFilesFilter ()
		{
			return data.FilterSet.AddAllFilesFilter ();
		}
		
		public bool HasFilters => data.FilterSet.HasFilters;

	    /// <summary>
		/// Adds the default file filters registered by MD core and addins. Includes the All Files filter.
		/// </summary>
		public void AddDefaultFileFilters ()
		{
			data.FilterSet.AddDefaultFileFilters ();
		}
		
		///<summary>Saves last default filter to MD prefs, if necessary</summary>
		protected void SaveDefaultFilter ()
		{
			data.FilterSet.SaveDefaultFilter ();
		}

		void SetGtkFileFilters (FileSelector fdiag)
		{
			var list = new List<FileFilter> ();
			FileFilter defaultGtkFilter = null;
			
			foreach (var filter in data.Filters) {
				var gf = new FileFilter ();
				if (!string.IsNullOrEmpty (filter.Name))
					gf.Name = filter.Name;
				if (filter.Patterns != null)
					foreach (var pattern in filter.Patterns)
						gf.AddPattern (pattern);
				if (filter.MimeTypes != null)
					foreach (var mimetype in filter.MimeTypes)
						gf.AddMimeType (mimetype);
				list.Add (gf);
				if (filter == DefaultFilter)
					defaultGtkFilter = gf;
			}
			
			foreach (var filter in list)
				fdiag.AddFilter (filter);
			
			if (defaultGtkFilter != null)
				fdiag.Filter = defaultGtkFilter;
			
			fdiag.Destroyed += CaptureDefaultFilter;
		}

		[ConnectBefore]
		void CaptureDefaultFilter (object sender, EventArgs e)
		{
			FileSelector fsel = (FileSelector) sender;
			if (fsel.Filter == null)
				return;
			var name = fsel.Filter.Name;
			foreach (var filter in data.Filters) {
				if (filter.Name == name) {
					data.DefaultFilter = filter;
					return;
				}
			}
		}
		
		#endregion
		
		/// <summary>
		/// Utility method to populate a GTK FileSelector from the data properties.
		/// </summary>
		internal void SetDefaultProperties (FileSelector fdiag)
		{
			fdiag.Title = Title;
			fdiag.Action = Action.ToGtkAction ();
			fdiag.LocalOnly = true;
			fdiag.SelectMultiple = SelectMultiple;
			fdiag.TransientFor = TransientFor;
			
			if (!CurrentFolder.IsNullOrEmpty)
				fdiag.SetCurrentFolder (CurrentFolder);
			if (!string.IsNullOrEmpty (InitialFileName))
				fdiag.CurrentName = InitialFileName;
			
			if (!CurrentFolder.IsNullOrEmpty && !string.IsNullOrEmpty (InitialFileName)) {
				var checkName = data.CurrentFolder.Combine (InitialFileName);
				if (File.Exists (checkName))
					fdiag.SetFilename (checkName);
			}
			
			SetGtkFileFilters (fdiag);
		}
		
		internal void GetDefaultProperties (FileSelector fdiag)
		{
			data.SelectedFiles = fdiag.Filenames.ToFilePathArray ();
			var currentFilter = fdiag.Filter;
			if (currentFilter != null) {
				var name = fdiag.Filter.Name;
				var def = data.Filters.FirstOrDefault (f => f.Name == name);
				if (def != null)
					DefaultFilter = def;
			}
		}
		
		public override bool Run ()
		{
			if (!HasFilters)
				AddDefaultFileFilters ();
			
			bool success = base.Run ();
			
			if (success)
				SaveDefaultFilter ();
			
			return success;
		}
		
		/// <summary>Runs the default implementation of the dialog.</summary>
		protected override bool RunDefault ()
		{
			var fdiag = new FileSelector ();
			SetDefaultProperties (fdiag);
			try {
				int result = MessageService.RunCustomDialog (fdiag, data.TransientFor ?? MessageService.RootWindow);
				GetDefaultProperties (fdiag);
				return result == (int) ResponseType.Ok;
			} finally {
				fdiag.Destroy ();
				fdiag.Dispose ();
			}
		}
	}
}
