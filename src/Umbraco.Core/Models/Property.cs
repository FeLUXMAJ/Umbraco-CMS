﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using Umbraco.Core.Models.EntityBase;

namespace Umbraco.Core.Models
{
    /// <summary>
    /// Represents a property.
    /// </summary>
    [Serializable]
    [DataContract(IsReference = true)]
    public class Property : Entity
    {
        private PropertyType _propertyType;
        private readonly PropertyTags _tagSupport = new PropertyTags(); // fixme allocating even if no support?

        private List<PropertyValue> _values = new List<PropertyValue>();
        private PropertyValue _pvalue;
        private Dictionary<int, PropertyValue> _lvalues;
        private Dictionary<int, Dictionary<string, PropertyValue>> _svalues;

        private static readonly Lazy<PropertySelectors> Ps = new Lazy<PropertySelectors>();

        protected Property()
        { }

        public Property(PropertyType propertyType)
        {
            _propertyType = propertyType;
        }

        public Property(int id, PropertyType propertyType)
        {
            Id = id;
            _propertyType = propertyType;
        }

        public class PropertyValue
        {
            public int? LanguageId { get; set; }
            public string Segment { get; set; }
            public object PublishedValue { get; set; }
            public object DraftValue { get; set; }

            public PropertyValue Clone()
                => new PropertyValue { LanguageId = LanguageId, Segment = Segment, PublishedValue = PublishedValue, DraftValue = DraftValue };
        }

        // ReSharper disable once ClassNeverInstantiated.Local
        private class PropertySelectors
        {
            public readonly PropertyInfo ValuesSelector = ExpressionHelper.GetPropertyInfo<Property, object>(x => x.Values);

            public readonly DelegateEqualityComparer<object> PropertyValueComparer = new DelegateEqualityComparer<object>(
                (o, o1) =>
                {
                    if (o == null && o1 == null) return true;

                    // custom comparer for strings.
                    // if one is null and another is empty then they are the same
                    if (o is string || o1 is string)
                        return ((o as string).IsNullOrWhiteSpace() && (o1 as string).IsNullOrWhiteSpace()) || (o != null && o1 != null && o.Equals(o1));

                    if (o == null || o1 == null) return false;

                    // custom comparer for enumerable
                    // ReSharper disable once MergeCastWithTypeCheck
                    if (o is IEnumerable && o1 is IEnumerable)
                        return ((IEnumerable) o).Cast<object>().UnsortedSequenceEqual(((IEnumerable) o1).Cast<object>());

                    return o.Equals(o1);
                }, o => o.GetHashCode());
        }

        /// <summary>
        /// Returns the PropertyType, which this Property is based on
        /// </summary>
        [IgnoreDataMember]
        public PropertyType PropertyType => _propertyType;

        /// <summary>
        /// Gets the list of values.
        /// </summary>
        [DataMember]
        public List<PropertyValue> Values
        {
            get => _values;
            set
            {
                _values = value;

                _pvalue = value.FirstOrDefault(x => !x.LanguageId.HasValue && x.Segment == null);

                _lvalues = value.Where(x => x.LanguageId.HasValue && x.Segment == null)
                    .ToDictionary(x => x.LanguageId.Value, x => x);

                _svalues = value.Where(x => x.LanguageId.HasValue && x.Segment != null)
                    .GroupBy(x => x.LanguageId.Value)
                    .ToDictionary(x => x.Key, x => x.ToDictionary(y => y.Segment, y => y));
            }
        }

        /// <summary>
        /// Returns the instance of the tag support, by default tags are not enabled
        /// </summary>
        internal PropertyTags TagSupport => _tagSupport;

        /// <summary>
        /// Returns the Alias of the PropertyType, which this Property is based on
        /// </summary>
        [DataMember]
        public string Alias => _propertyType.Alias;

        /// <summary>
        /// Returns the Id of the PropertyType, which this Property is based on
        /// </summary>
        [IgnoreDataMember]
        internal int PropertyTypeId => _propertyType.Id;

        /// <summary>
        /// Returns the DatabaseType that the underlaying DataType is using to store its values
        /// </summary>
        /// <remarks>
        /// Only used internally when saving the property value.
        /// </remarks>
        [IgnoreDataMember]
        internal DataTypeDatabaseType DataTypeDatabaseType => _propertyType.DataTypeDatabaseType;

        /// <summary>
        /// Gets the neutral value.
        /// </summary>
        public object GetValue(bool published = false)
        {
            if (_pvalue == null) return null;
            return published ? _pvalue.PublishedValue : _pvalue.DraftValue;
        }

        /// <summary>
        /// Gets the culture value.
        /// </summary>
        public object GetValue(int languageId, bool published = false)
        {
            if (_lvalues == null) return null;
            if (!_lvalues.TryGetValue(languageId, out var value)) return null;
            return published ? value.PublishedValue : value.DraftValue;
        }

        /// <summary>
        /// Gets the segment value.
        /// </summary>
        public object GetValue(int languageId, string segment, bool published = false)
        {
            if (_svalues == null) return null;
            if (!_svalues.TryGetValue(languageId, out var svalues)) return null;
            if (!svalues.TryGetValue(segment, out var value)) return null;
            return published ? value.PublishedValue : value.DraftValue;
        }

        internal void PublishValues()
        {
            (var pvalue, _) = GetPropertyValue(false);
            if (pvalue == null) return;
            PublishPropertyValue(pvalue);
        }

        internal void PublishValues(int? nLanguageId)
        {
            if (nLanguageId == null)
            {
                PublishValues();
                return;
            }

            var languageId = nLanguageId.Value;

            (var pvalue, _) = GetPropertyValue(languageId, false);
            if (pvalue == null) return;
            PublishPropertyValue(pvalue);
        }

        internal void PublishValues(int? nLanguageId, string segment)
        {
            if (segment == null)
            {
                PublishValues(nLanguageId);
                return;
            }

            if (!nLanguageId.HasValue)
                throw new ArgumentException("Cannot be null when segment is not null.", nameof(nLanguageId));

            var languageId = nLanguageId.Value;

            (var pvalue, _) = GetPropertyValue(languageId, segment, false);
            if (pvalue == null) return;
            PublishPropertyValue(pvalue);
        }

        internal void PublishAllValues()
        {
            foreach (var pvalue in Values)
                PublishPropertyValue(pvalue);
        }

        private void PublishPropertyValue(PropertyValue pvalue)
        {
            var origValue = pvalue.PublishedValue;
            pvalue.PublishedValue = ConvertSetValue(pvalue.DraftValue);
            DetectChanges(pvalue.DraftValue, origValue, Ps.Value.ValuesSelector, Ps.Value.PropertyValueComparer, false);
        }

        /// <summary>
        /// Sets a (draft) neutral value.
        /// </summary>
        public void SetValue(object value)
        {
            (var pvalue, var change) = GetPropertyValue(true);

            var origValue = pvalue.DraftValue;
            pvalue.DraftValue = ConvertSetValue(value);

            DetectChanges(_pvalue.DraftValue, origValue, Ps.Value.ValuesSelector, Ps.Value.PropertyValueComparer, change);
        }

        /// <summary>
        /// Sets a (draft) culture value.
        /// </summary>
        public void SetValue(int? nLanguageId, object value)
        {
            if (nLanguageId == null)
            {
                SetValue(value);
                return;
            }

            var languageId = nLanguageId.Value;

            (var pvalue, var change) = GetPropertyValue(languageId, true);

            var origValue = pvalue.DraftValue;
            pvalue.DraftValue = ConvertSetValue(value);

            DetectChanges(pvalue.DraftValue, origValue, Ps.Value.ValuesSelector, Ps.Value.PropertyValueComparer, change);
        }

        /// <summary>
        /// Sets a (draft) segment value.
        /// </summary>
        public void SetValue(int? nLanguageId, string segment, object value)
        {
            if (segment == null)
            {
                SetValue(nLanguageId, value);
                return;
            }

            if (!nLanguageId.HasValue)
                throw new ArgumentException("Cannot be null when segment is not null.", nameof(nLanguageId));
            var languageId = nLanguageId.Value;

            (var pvalue, var change) = GetPropertyValue(languageId, segment, true);

            var origValue = pvalue.DraftValue;
            pvalue.DraftValue = ConvertSetValue(value);

            DetectChanges(pvalue.DraftValue, origValue, Ps.Value.ValuesSelector, Ps.Value.PropertyValueComparer, change);
        }

        private (PropertyValue, bool) GetPropertyValue(bool create)
        {
            var change = false;
            if (_pvalue == null)
            {
                if (!create) return (null, false);
                _pvalue = new PropertyValue();
                _values.Add(_pvalue);
                change = true;
            }
            return (_pvalue, change);
        }

        private (PropertyValue, bool) GetPropertyValue(int languageId, bool create)
        {
            var change = false;
            if (_lvalues == null)
            {
                if (!create) return (null, false);
                _lvalues = new Dictionary<int, PropertyValue>();
                change = true;
            }
            if (!_lvalues.TryGetValue(languageId, out var pvalue))
            {
                if (!create) return (null, false);
                pvalue = _lvalues[languageId] = new PropertyValue();
                _values.Add(pvalue);
                change = true;
            }
            return (pvalue, change);
        }

        private (PropertyValue, bool) GetPropertyValue(int languageId, string segment, bool create)
        {
            var change = false;
            if (_svalues == null)
            {
                if (!create) return (null, false);
                _svalues = new Dictionary<int, Dictionary<string, PropertyValue>>();
                change = true;
            }
            if (!_svalues.TryGetValue(languageId, out var svalue))
            {
                if (!create) return (null, false);
                svalue = _svalues[languageId] = new Dictionary<string, PropertyValue>();
                change = true;
            }
            if (!svalue.TryGetValue(segment, out var pvalue))
            {
                if (!create) return (null, false);
                pvalue = svalue[segment] = new PropertyValue();
                _values.Add(pvalue);
                change = true;
            }
            return (pvalue, change);
        }

        private object ConvertSetValue(object value)
        {
            var isOfExpectedType = _propertyType.IsPropertyTypeValid(value);

            if (isOfExpectedType)
                return value;

            // isOfExpectedType is true if value is null - so if false, value is *not* null
            // "garbage-in", accept what we can & convert
            // throw only if conversion is not possible

            var s = value.ToString();

            switch (_propertyType.DataTypeDatabaseType)
            {
                case DataTypeDatabaseType.Nvarchar:
                case DataTypeDatabaseType.Ntext:
                    return s;

                case DataTypeDatabaseType.Integer:
                    if (s.IsNullOrWhiteSpace())
                        return null; // assume empty means null
                    var convInt = value.TryConvertTo<int>();
                    if (convInt == false) ThrowTypeException(value, typeof(int), _propertyType.Alias);
                    return convInt.Result;

                case DataTypeDatabaseType.Decimal:
                    if (s.IsNullOrWhiteSpace())
                        return null; // assume empty means null
                    var convDecimal = value.TryConvertTo<decimal>();
                    if (convDecimal == false) ThrowTypeException(value, typeof(decimal), _propertyType.Alias);
                    // need to normalize the value (change the scaling factor and remove trailing zeroes)
                    // because the underlying database is going to mess with the scaling factor anyways.
                    return convDecimal.Result.Normalize();

                case DataTypeDatabaseType.Date:
                    if (s.IsNullOrWhiteSpace())
                        return null; // assume empty means null
                    var convDateTime = value.TryConvertTo<DateTime>();
                    if (convDateTime == false) ThrowTypeException(value, typeof(DateTime), _propertyType.Alias);
                    return convDateTime.Result;
            }

            return value;
        }

        private static void ThrowTypeException(object value, Type expected, string alias)
        {
            throw new InvalidOperationException($"Cannot assign value \"{value}\" of type \"{value.GetType()}\" to property \"{alias}\" expecting type \"{expected}\".");
        }

        /// <summary>
        /// Gets a value indicating whether the (draft) neutral value is valid.
        /// </summary>
        /// <remarks>An invalid value can be saved, but only valid values can be published.</remarks>
        public bool IsValid()
        {
            return IsValid(GetValue());
        }

        /// <summary>
        /// Gets a value indicating whether the (draft) culture value is valid.
        /// </summary>
        /// <remarks>An invalid value can be saved, but only valid values can be published.</remarks>
        public bool IsValid(int languageId)
        {
            return IsValid(GetValue(languageId));
        }

        /// <summary>
        /// Gets a value indicating whether the (draft) segment value is valid.
        /// </summary>
        /// <remarks>An invalid value can be saved, but only valid values can be published.</remarks>
        public bool IsValue(int languageId, string segment)
        {
            return IsValid(GetValue(languageId, segment));
        }

        /// <summary>
        /// Boolean indicating whether the passed in value is valid
        /// </summary>
        /// <param name="value"></param>
        /// <returns>True is property value is valid, otherwise false</returns>
        private bool IsValid(object value)
        {
            return _propertyType.IsValidPropertyValue(value);
        }

        public override object DeepClone()
        {
            var clone = (Property) base.DeepClone();

            //turn off change tracking
            clone.DisableChangeTracking();

            //need to manually assign since this is a readonly property
            clone._propertyType = (PropertyType) PropertyType.DeepClone();

            //re-enable tracking
            clone.ResetDirtyProperties(false); // not needed really, since we're not tracking
            clone.EnableChangeTracking();

            return clone;
        }
    }
}
