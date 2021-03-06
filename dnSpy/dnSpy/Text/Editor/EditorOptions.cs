﻿/*
    Copyright (C) 2014-2018 de4dot@gmail.com

    This file is part of dnSpy

    dnSpy is free software: you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.

    dnSpy is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with dnSpy.  If not, see <http://www.gnu.org/licenses/>.
*/

using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Utilities;

namespace dnSpy.Text.Editor {
	sealed class EditorOptions : IEditorOptions {
		public IEditorOptions GlobalOptions => service.GlobalOptions;

		public IEditorOptions Parent {
			get { return parent; }
			set {
				// Check if we're the global options
				if (parent == null)
					throw new InvalidOperationException();
				if (value == null)
					throw new ArgumentNullException(nameof(value));
				if (parent == value)
					return;
				var oldParent = parent;
				parent = (EditorOptions)value;
				UpdateOptions(oldParent);
			}
		}
		EditorOptions parent;

		public IEnumerable<EditorOptionDefinition> SupportedOptions {
			get {
				foreach (var def in service.EditorOptionDefinitions) {
					if (scope == null || def.IsApplicableToScope(scope))
						yield return def;
				}
			}
		}

		readonly Dictionary<string, object> dict;
		readonly EditorOptionsFactoryService service;
		readonly List<WeakReference> weakChildren;
		readonly IPropertyOwner scope;

		public EditorOptions(EditorOptionsFactoryService service, EditorOptions parent, IPropertyOwner scope) {
			this.service = service;
			this.parent = parent;
			dict = new Dictionary<string, object>(StringComparer.Ordinal);
			weakChildren = new List<WeakReference>();
			this.scope = scope;
			UpdateOptions(null);
		}

		void UpdateOptions(EditorOptions oldParent) {
			if (oldParent != null) {
				for (int i = 0; i < oldParent.weakChildren.Count; i++) {
					if (oldParent.weakChildren[i].Target == this) {
						oldParent.weakChildren.RemoveAt(i);
						break;
					}
				}
			}
			if (parent != null)
				parent.weakChildren.Add(new WeakReference(this));

			if (parent != null || oldParent != null) {
				foreach (var o in SupportedOptions) {
					if (dict.ContainsKey(o.Name))
						continue;
					var oldValue = oldParent == null ? o.DefaultValue : oldParent.GetValueOrDefault(o.Name);
					var newValue = parent == null ? o.DefaultValue : parent.GetValueOrDefault(o.Name);
					if (!Equals(oldValue, newValue))
						OnChanged(o.Name);
				}
			}
		}

		bool TryGetValue(string optionId, out object value) {
			if (scope != null && !service.GetOption(optionId).IsApplicableToScope(scope))
				throw new InvalidOperationException();
			var p = this;
			while (p != null) {
				if (p.dict.TryGetValue(optionId, out value))
					return true;
				p = p.parent;
			}
			value = null;
			return false;
		}

		object GetValueOrDefault(string optionId) {
			if (!TryGetValue(optionId, out object value))
				value = service.GetOption(optionId).DefaultValue;
			return value;
		}

		public event EventHandler<EditorOptionChangedEventArgs> OptionChanged;
		void OnChanged(string optionId) {
			if (scope == null || service.GetOption(optionId).IsApplicableToScope(scope))
				OptionChanged?.Invoke(this, new EditorOptionChangedEventArgs(optionId));
			for (int i = weakChildren.Count - 1; i >= 0; i--) {
				var child = weakChildren[i].Target as EditorOptions;
				if (child == null) {
					weakChildren.RemoveAt(i);
					continue;
				}
				if (!child.dict.ContainsKey(optionId))
					child.OnChanged(optionId);
			}
		}

		public bool IsOptionDefined<T>(EditorOptionKey<T> key, bool localScopeOnly) => IsOptionDefined(key.Name, localScopeOnly);
		public bool IsOptionDefined(string optionId, bool localScopeOnly) {
			if (optionId == null)
				throw new ArgumentNullException(nameof(optionId));
			if (parent != null && localScopeOnly)
				return dict.ContainsKey(optionId);
			var def = service.GetOption(optionId);
			return scope == null || def.IsApplicableToScope(scope);
		}

		public bool ClearOptionValue<T>(EditorOptionKey<T> key) => ClearOptionValue(key.Name);
		public bool ClearOptionValue(string optionId) {
			if (optionId == null)
				throw new ArgumentNullException(nameof(optionId));
			if (parent == null || !dict.TryGetValue(optionId, out object oldValue))
				return false;
			dict.Remove(optionId);
			var newValue = GetValueOrDefault(optionId);
			if (!Equals(oldValue, newValue))
				OnChanged(optionId);
			return true;
		}

		public T GetOptionValue<T>(string optionId) => (T)GetOptionValue(optionId);
		public T GetOptionValue<T>(EditorOptionKey<T> key) => (T)GetOptionValue(key.Name);
		public object GetOptionValue(string optionId) {
			if (optionId == null)
				throw new ArgumentNullException(nameof(optionId));
			return GetValueOrDefault(optionId);
		}

		public void SetOptionValue<T>(EditorOptionKey<T> key, T value) => SetOptionValue(key.Name, value);
		public void SetOptionValue(string optionId, object value) {
			if (optionId == null)
				throw new ArgumentNullException(nameof(optionId));
			var def = service.GetOption(optionId);
			if (scope != null && !def.IsApplicableToScope(scope))
				throw new InvalidOperationException();
			if (!def.IsValid(ref value))
				throw new ArgumentException();
			var oldValue = GetValueOrDefault(optionId);
			dict[optionId] = value;
			if (!Equals(oldValue, value))
				OnChanged(optionId);
		}
	}
}
