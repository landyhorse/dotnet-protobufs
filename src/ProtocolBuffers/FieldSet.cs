// Protocol Buffers - Google's data interchange format
// Copyright 2008 Google Inc.  All rights reserved.
// http://github.com/jskeet/dotnet-protobufs/
// Original C++/Java/Python code:
// http://code.google.com/p/protobuf/
//
// Redistribution and use in source and binary forms, with or without
// modification, are permitted provided that the following conditions are
// met:
//
//     * Redistributions of source code must retain the above copyright
// notice, this list of conditions and the following disclaimer.
//     * Redistributions in binary form must reproduce the above
// copyright notice, this list of conditions and the following disclaimer
// in the documentation and/or other materials provided with the
// distribution.
//     * Neither the name of Google Inc. nor the names of its
// contributors may be used to endorse or promote products derived from
// this software without specific prior written permission.
//
// THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS
// "AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT
// LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR
// A PARTICULAR PURPOSE ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT
// OWNER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL,
// SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT
// LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE,
// DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY
// THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
// (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE
// OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
using System;
using System.Collections;
using System.Collections.Generic;
using Google.ProtocolBuffers.Collections;
using Google.ProtocolBuffers.Descriptors;

namespace Google.ProtocolBuffers {
  /// <summary>
  /// A class which represents an arbitrary set of fields of some message type.
  /// This is used to implement DynamicMessage, and also to represent extensions
  /// in GeneratedMessage. This class is internal, since outside users should probably
  /// be using DynamicMessage.
  /// 
  /// As in the Java implementation, this class goes against the rest of the framework
  /// in terms of mutability. Instead of having a mutable Builder class and an immutable
  /// FieldSet class, FieldSet just has a MakeImmutable() method. This is safe so long as
  /// all callers are careful not to let a mutable FieldSet escape into the open. This would
  /// be impossible to guarantee if this were a public class, of course.
  /// 
  /// All repeated fields are stored as IList[object] even 
  /// TODO(jonskeet): Finish this comment!
  /// </summary>
  internal sealed class FieldSet {

    private static readonly FieldSet defaultInstance = new FieldSet(new Dictionary<FieldDescriptor, object>()).MakeImmutable();

    private IDictionary<FieldDescriptor, object> fields;

    private FieldSet(IDictionary<FieldDescriptor, object> fields) {
      this.fields = fields;
    }

    public static FieldSet CreateInstance() {
      // Use SortedList to keep fields in the canonical order
      return new FieldSet(new SortedList<FieldDescriptor, object>());
    }

    /// <summary>
    /// Makes this FieldSet immutable, and returns it for convenience. Any
    /// mutable repeated fields are made immutable, as well as the map itself.
    /// </summary>
    internal FieldSet MakeImmutable() {
      // First check if we have any repeated values
      bool hasRepeats = false;
      foreach (object value in fields.Values) {
        IList<object> list = value as IList<object>;
        if (list != null && !list.IsReadOnly) {
          hasRepeats = true;
          break;
        }
      }

      if (hasRepeats) {
        var tmp = new SortedList<FieldDescriptor, object>();
        foreach (KeyValuePair<FieldDescriptor, object> entry in fields) {
          IList<object> list = entry.Value as IList<object>;
          tmp[entry.Key] = list == null ? entry.Value : Lists.AsReadOnly(list);
        }
        fields = tmp;
      }

      fields = Dictionaries.AsReadOnly(fields);

      return this;
    }

    /// <summary>
    /// Returns the default, immutable instance with no fields defined.
    /// </summary>
    internal static FieldSet DefaultInstance {
      get { return defaultInstance; }
    }

    /// <summary>
    /// Returns an immutable mapping of fields. Note that although the mapping itself
    /// is immutable, the entries may not be (i.e. any repeated values are represented by
    /// mutable lists). The behaviour is not specified if the contents are mutated.
    /// </summary>
    internal IDictionary<FieldDescriptor, object> AllFields {
      get { return Dictionaries.AsReadOnly(fields); }
    }

    /// <summary>
    /// See <see cref="IMessage.HasField"/>.
    /// </summary>
    public bool HasField(FieldDescriptor field) {
      if (field.IsRepeated) {
        throw new ArgumentException("HasField() can only be called on non-repeated fields.");
      }

      return fields.ContainsKey(field);
    }

    /// <summary>
    /// Clears all fields.
    /// </summary>
    internal void Clear() {
      fields.Clear();
    }

    /// <summary>
    /// See <see cref="IMessage.Item(FieldDescriptor)"/>
    /// </summary>
    /// <remarks>
    /// If the field is not set, the behaviour when fetching this property varies by field type:
    /// <list>
    /// <item>For singular message values, null is returned.</item>
    /// <item>For singular non-message values, the default value of the field is returned.</item>
    /// <item>For repeated values, an empty immutable list is returned. This will be compatible
    /// with IList[object], regardless of the type of the repeated item.</item>
    /// </list>
    /// This method returns null if the field is a singular message type
    /// and is not set; in this case it is up to the caller to fetch the 
    /// message's default instance. For repeated fields of message types, 
    /// an empty collection is returned. For repeated fields of non-message
    /// types, null is returned.
    /// <para />
    /// When setting this property, any list values are copied, and each element is checked
    /// to ensure it is of an appropriate type.
    /// </remarks>
    /// 
    internal object this[FieldDescriptor field] {
      get {
        object result;
        if (fields.TryGetValue(field, out result)) {
          return result;
        }
        if (field.MappedType == MappedType.Message) {
          if (field.IsRepeated) {
            return new List<object>();
          } else {
            return null;
          }
        }
        return field.DefaultValue;
      }
      set {
        if (field.IsRepeated) {
          List<object> list = value as List<object>;
          if (list == null) {
            throw new ArgumentException("Wrong object type used with protocol message reflection.");
          }

          // Wrap the contents in a new list so that the caller cannot change
          // the list's contents after setting it.
          List<object> newList = new List<object>(list);
          foreach (object element in newList) {
            VerifyType(field, element);
          }
          value = newList;
        }
        else {
          VerifyType(field, value);
        }
        fields[field] = value;
      }
    }

    /// <summary>
    /// See <see cref="IMessage.Item(FieldDescriptor,int)" />
    /// </summary>
    internal object this[FieldDescriptor field, int index] {
      get {
        if (!field.IsRepeated) {
          throw new ArgumentException("Indexer specifying field and index can only be called on repeated fields.");
        }

        return ((IList<object>) this[field])[index];
      }
      set {
        if (!field.IsRepeated) {
          throw new ArgumentException("Indexer specifying field and index can only be called on repeated fields.");
        }
        VerifyType(field, value);
        object list;
        if (!fields.TryGetValue(field, out list)) {
          throw new ArgumentOutOfRangeException();
        }
        ((IList<object>) list)[index] = value;
      }
    }

    /// <summary>
    /// See <see cref="IBuilder{TMessage, TBuilder}.AddRepeatedField" />
    /// </summary>
    internal void AddRepeatedField(FieldDescriptor field, object value) {
      if (!field.IsRepeated) {
        throw new ArgumentException("AddRepeatedField can only be called on repeated fields.");
      }
      VerifyType(field, value);
      object list;
      if (!fields.TryGetValue(field, out list)) {
        list = new List<object>();
        fields[field] = list;
      }
      ((IList<object>) list).Add(value);
    }

    /// <summary>
    /// Returns an enumerator for the field map. Used to write the fields out.
    /// </summary>
    internal IEnumerator<KeyValuePair<FieldDescriptor, object>> GetEnumerator() {
      return fields.GetEnumerator();
    }

    /// <summary>
    /// See <see cref="IMessage.IsInitialized" />
    /// </summary>
    /// <remarks>
    /// Since FieldSet itself does not have any way of knowing about
    /// required fields that aren't actually present in the set, it is up
    /// to the caller to check for genuinely required fields. This property
    /// merely checks that any messages present are themselves initialized.
    /// </remarks>
    internal bool IsInitialized {
      get {
        foreach (KeyValuePair<FieldDescriptor, object> entry in fields) {
          FieldDescriptor field = entry.Key;
          if (field.MappedType == MappedType.Message) {
            if (field.IsRepeated) {
              foreach(IMessage message in (IEnumerable) entry.Value) {
                if (!message.IsInitialized) {
                  return false;
                }
              }
            } else {
              if (!((IMessage) entry.Value).IsInitialized) {
                return false;
              }
            }
          }
        }
        return true;
      }
    }

    /// <summary>
    /// Verifies whether all the required fields in the specified message
    /// descriptor are present in this field set, as well as whether
    /// all the embedded messages are themselves initialized.
    /// </summary>
    internal bool IsInitializedWithRespectTo(MessageDescriptor type) {
      foreach (FieldDescriptor field in type.Fields) {
        if (field.IsRequired && !HasField(field)) {
          return false;
        }
      }
      return IsInitialized;
    }

    /// <summary>
    /// See <see cref="IBuilder{TMessage, TBuilder}.ClearField" />
    /// </summary>
    public void ClearField(FieldDescriptor field) {
      fields.Remove(field);
    }

    /// <summary>
    /// See <see cref="IMessage.GetRepeatedFieldCount" />
    /// </summary>
    public int GetRepeatedFieldCount(FieldDescriptor field) {
      if (!field.IsRepeated) {
        throw new ArgumentException("GetRepeatedFieldCount() can only be called on repeated fields.");
      }

      return ((IList<object>) this[field]).Count;
    }

    /// <summary>
    /// Implementation of both <c>MergeFrom</c> methods.
    /// </summary>
    /// <param name="otherFields"></param>
    private void MergeFields(IEnumerable<KeyValuePair<FieldDescriptor, object>> otherFields) {
      // Note:  We don't attempt to verify that other's fields have valid
      //   types.  Doing so would be a losing battle.  We'd have to verify
      //   all sub-messages as well, and we'd have to make copies of all of
      //   them to insure that they don't change after verification (since
      //   the IMessage interface itself cannot enforce immutability of
      //   implementations).
      // TODO(jonskeet):  Provide a function somewhere called MakeDeepCopy()
      //   which allows people to make secure deep copies of messages.

      foreach (KeyValuePair<FieldDescriptor, object> entry in otherFields) {
        FieldDescriptor field = entry.Key;
        object existingValue;
        fields.TryGetValue(field, out existingValue);
        if (field.IsRepeated) {
          if (existingValue == null) {
            existingValue = new List<object>();
            fields[field] = existingValue;
          }
          IList<object> list = (IList<object>) existingValue;
          foreach (object otherValue in (IEnumerable) entry.Value) {
            list.Add(otherValue);
          }
        } else if (field.MappedType == MappedType.Message && existingValue != null) {
          IMessage existingMessage = (IMessage)existingValue;
          IMessage merged = existingMessage.WeakToBuilder()
              .WeakMergeFrom((IMessage) entry.Value)
              .WeakBuild();
          this[field] = merged;
        } else {
          this[field] = entry.Value;
        }
      }
    }

    /// <summary>
    /// See <see cref="IBuilder{TMessage, TBuilder}.MergeFrom(IMessage)" />
    /// </summary>
    public void MergeFrom(IMessage other) {
      MergeFields(other.AllFields);
    }

    /// <summary>
    /// Like <see cref="MergeFrom(IMessage)"/>, but merges from another <c>FieldSet</c>.
    /// </summary>
    public void MergeFrom(FieldSet other) {
      MergeFields(other.fields);
    }

    /// <summary>
    /// See <see cref="IMessage.WriteTo(CodedOutputStream)" />.
    /// </summary>
    public void WriteTo(CodedOutputStream output) {
      foreach (KeyValuePair<FieldDescriptor, object> entry in fields) {
        WriteField(entry.Key, entry.Value, output);
      }
    }

    /// <summary>
    /// Writes a single field to a CodedOutputStream.
    /// </summary>
    public void WriteField(FieldDescriptor field, Object value, CodedOutputStream output) {
      if (field.IsExtension && field.ContainingType.Options.MessageSetWireFormat) {
        output.WriteMessageSetExtension(field.FieldNumber, (IMessage) value);
      } else {
        if (field.IsRepeated) {
          IEnumerable valueList = (IEnumerable) value;
          if (field.IsPacked) {
            output.WriteTag(field.FieldNumber, WireFormat.WireType.LengthDelimited);
            // Compute the total data size so the length can be written.
            int dataSize = 0;
            foreach (object element in valueList) {
              dataSize += CodedOutputStream.ComputeFieldSizeNoTag(field.FieldType, element);
            }
            output.WriteRawVarint32((uint)dataSize);
            // Write the data itself, without any tags.
            foreach (object element in valueList) {
              output.WriteFieldNoTag(field.FieldType, element);
            }
          } else {
            foreach (object element in valueList) {
              output.WriteField(field.FieldType, field.FieldNumber, element);
            }
          }
        } else {
          output.WriteField(field.FieldType, field.FieldNumber, value);
        }
      }
    }

    /// <summary>
    /// See <see cref="IMessage.SerializedSize" />. It's up to the caller to
    /// cache the resulting size if desired.
    /// </summary>
    public int SerializedSize {
      get {
        int size = 0;
        foreach (KeyValuePair<FieldDescriptor, object> entry in fields) {
          FieldDescriptor field = entry.Key;
          object value = entry.Value;

          if (field.IsExtension && field.ContainingType.Options.MessageSetWireFormat) {
            size += CodedOutputStream.ComputeMessageSetExtensionSize(field.FieldNumber, (IMessage)value);
          } else {
            if (field.IsRepeated) {
              IEnumerable valueList = (IEnumerable)value;
              if (field.IsPacked) {
                int dataSize = 0;
                foreach (object element in valueList) {
                  dataSize += CodedOutputStream.ComputeFieldSizeNoTag(field.FieldType, element);
                }
                size += dataSize + CodedOutputStream.ComputeTagSize(field.FieldNumber) + CodedOutputStream.ComputeRawVarint32Size((uint)dataSize);
              } else {
                foreach (object element in valueList) {
                  size += CodedOutputStream.ComputeFieldSize(field.FieldType, field.FieldNumber, element);
                }
              }
            } else {
              size += CodedOutputStream.ComputeFieldSize(field.FieldType, field.FieldNumber, value);
            }
          }
        }
        return size;
      }
    }

    /// <summary>
    /// Verifies that the given object is of the correct type to be a valid
    /// value for the given field.
    /// </summary>
    /// <remarks>
    /// For repeated fields, this checks if the object is of the right
    /// element type, not whether it's a list.
    /// </remarks>
    /// <exception cref="ArgumentException">The value is not of the right type.</exception>
    /// <exception cref="ArgumentNullException">The value is null.</exception>
    private static void VerifyType(FieldDescriptor field, object value) {
      ThrowHelper.ThrowIfNull(value, "value");
      bool isValid = false;
      switch (field.MappedType) {
        case MappedType.Int32:       isValid = value is int;    break;
        case MappedType.Int64:       isValid = value is long;   break;
        case MappedType.UInt32:      isValid = value is uint;   break;
        case MappedType.UInt64:      isValid = value is ulong;  break;
        case MappedType.Single:      isValid = value is float;  break;
        case MappedType.Double:      isValid = value is double; break;
        case MappedType.Boolean:     isValid = value is bool;   break;
        case MappedType.String:      isValid = value is string; break;
        case MappedType.ByteString:  isValid = value is ByteString; break;        
        case MappedType.Enum:
          EnumValueDescriptor enumValue = value as EnumValueDescriptor;
          isValid = enumValue != null && enumValue.EnumDescriptor == field.EnumType;
          break;
        case MappedType.Message:
          IMessage messageValue = value as IMessage;
          isValid = messageValue != null && messageValue.DescriptorForType == field.MessageType;
          break;
      }

      if (!isValid) {
        // When chaining calls to SetField(), it can be hard to tell from
        // the stack trace which exact call failed, since the whole chain is
        // considered one line of code.  So, let's make sure to include the
        // field name and other useful info in the exception.
        throw new ArgumentException("Wrong object type used with protocol message reflection. "
            + "Message type \"" + field.ContainingType.FullName
            + "\", field \"" + (field.IsExtension ? field.FullName : field.Name)
            + "\", value was type \"" + value.GetType().Name + "\".");
      }
    }     
  }
}
