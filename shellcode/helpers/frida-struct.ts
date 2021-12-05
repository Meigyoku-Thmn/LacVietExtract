type Int8Field = { name: 'int8' };
type UInt8Field = { name: 'uint8' };
type Int16Field = { name: 'int16' };
type UInt16Field = { name: 'uint16' };
type Int32Field = { name: 'int32' };
type UInt32Field = { name: 'uint32' };
type Int64Field = { name: 'int64' };
type UInt64Field = { name: 'uint64' };
type FloatField = { name: 'float' };
type DoubleField = { name: 'double' };
type StringField = { name: 'str' | 'wstr' | 'ustr', length: number };
type PointerField = { name: 'pointer' };
type Boolean32Field = { name: 'bool32' };
type Boolean8Field = { name: 'bool8' };

type SignedNumberField = Int8Field | Int16Field | Int32Field;
type UnsignedNumberField = UInt8Field | UInt16Field | UInt32Field;
type FloatingPointField = FloatField | DoubleField;
type BooleanField = Boolean8Field | Boolean32Field;

type StructScalarField =
   Int8Field | UInt8Field | Int16Field | UInt16Field | Int32Field | UInt32Field |
   Int64Field | UInt64Field | FloatField | DoubleField |
   StringField | PointerField | Boolean32Field | Boolean8Field;

type StructField = Struct | StructScalarField | [StructField, number];

type Struct = {
   [field: string]: StructField;
};

const PlainNumberTypes: StructScalarField['name'][] = [
   'int8', 'uint8', 'int16', 'uint16', 'int32', 'uint32', 'float', 'double'];
const BigNumberTypes: StructScalarField['name'][] = ['int64', 'uint64'];
const BooleanTypes: StructScalarField['name'][] = ['bool32', 'bool8'];
const StringTypes: StructScalarField['name'][] = ['str', 'wstr', 'ustr'];

class ScalarArray<Hint extends StructScalarField['name'], RetType> {
   public length: number;
   constructor(
      type: Hint,
      sample: RetType,
      address: NativePointer,
      typeInfo: TypeInfo,
      size: number,
   ) {
      const elementSize = typeInfo.size.call(null) as number;
      this.length = size / elementSize;
      for (let i = 0; i < this.length; i++) {
         const fieldContext = {
            offset: elementSize * i,
            get ptr() { return address; },
         } as FieldContext;
         Object.defineProperty(this, i, {
            enumerable: true,
            get: typeInfo.get.bind(fieldContext),
            set: typeInfo.set.bind(fieldContext),
         });
      }
   }
   [index: number]: RetType;
   [Symbol.iterator]() {
      let index = 0;
      let instance = this;
      return {
         next() {
            if (index >= instance.length) return { value: undefined, done: true };
            else return { value: instance[index++], done: false };
         }
      }
   }
}
function wrapArray(type: StructScalarField['name'], address: NativePointer, typeInfo: TypeInfo, size: number) {
   if (PlainNumberTypes.indexOf(type) != -1)
      return new ScalarArray(type, 0, address, typeInfo, size);
   if (BigNumberTypes.indexOf(type) != -1)
      return new ScalarArray(type, BigInt(0), address, typeInfo, size);
   if (BooleanTypes.indexOf(type) != -1)
      return new ScalarArray(type, false, address, typeInfo, size);
   if (type == 'pointer')
      return new ScalarArray(type, ptr(0), address, typeInfo, size);
}

class StructArray<RetType extends StructInstance<Struct>> {
   public length: number;
   constructor(arr: RetType[]) {
      this.length = arr.length;
      for (let i = 0; i < this.length; i++) {
         const _i = i;
         Object.defineProperty(this, i, {
            enumerable: true,
            get: () => arr[_i],
         });
      }
   }
   [index: number]: RetType;
   [Symbol.iterator]() {
      let index = 0;
      let instance = this;
      return {
         next() {
            if (index >= instance.length) return { value: undefined, done: true };
            else return { value: instance[index++], done: false };
         }
      }
   }
}

export const pointer: PointerField = { name: 'pointer' };
export const uint64: UInt64Field = { name: 'uint64' };
export const int64: Int64Field = { name: 'int64' };
export const uint32: UInt32Field = { name: 'uint32' };
export const int32: Int32Field = { name: 'int32' };
export const uint16: UInt16Field = { name: 'uint16' };
export const int16: Int16Field = { name: 'int16' };
export const uint8: UInt8Field = { name: 'uint8' };
export const int8: Int8Field = { name: 'int8' };
export const bool32: Boolean32Field = { name: 'bool32' };
export const bool8: Boolean8Field = { name: 'bool8' };
export const text = (size: number): StringField => ({ name: 'str', length: size });
export const utext = (size: number): StringField => ({ name: 'ustr', length: size });
export const wtext = (length: number): StringField => ({ name: 'wstr', length });
export const arr = <T extends StructField>(type: T, length: number) => [type, length] as [T, number];

type StructInstanceData<T extends Struct> = {
   [Field in keyof T]:
   T[Field] extends SignedNumberField
   ? number
   : T[Field] extends UnsignedNumberField
   ? number
   : T[Field] extends Int64Field
   ? Int64
   : T[Field] extends UInt64Field
   ? UInt64
   : T[Field] extends FloatingPointField
   ? number
   : T[Field] extends StringField
   ? string
   : T[Field] extends PointerField
   ? NativePointer
   : T[Field] extends BooleanField
   ? boolean
   : T[Field] extends Struct
   ? StructInstance<T[Field]>
   : T[Field] extends [infer ElementType, number]
   ? (
      ElementType extends Int8Field
      ? ScalarArray<'int8', number>
      : ElementType extends UInt8Field
      ? ScalarArray<'uint8', number>
      : ElementType extends Int16Field
      ? ScalarArray<'int16', number>
      : ElementType extends UInt16Field
      ? ScalarArray<'uint16', number>
      : ElementType extends Int32Field
      ? ScalarArray<'int32', number>
      : ElementType extends UInt32Field
      ? ScalarArray<'uint32', number>
      : ElementType extends Int64Field
      ? ScalarArray<'int64', number>
      : ElementType extends UInt64Field
      ? ScalarArray<'uint64', number>
      : ElementType extends FloatField
      ? ScalarArray<'float', number>
      : ElementType extends DoubleField
      ? ScalarArray<'double', number>
      : ElementType extends Boolean32Field
      ? ScalarArray<'bool32', boolean>
      : ElementType extends Boolean8Field
      ? ScalarArray<'bool8', boolean>
      : ElementType extends PointerField
      ? ScalarArray<'pointer', NativePointer>
      : ElementType extends Struct
      ? StructArray<StructInstance<ElementType>>
      : never
   )
   : never;
};

export type StructInstance<T extends Struct> = StructInstanceData<T> & {
   getPtr: () => NativePointer,
   getSize: () => number,
   setPtr?: (ptr: NativePointer) => void,
};

type NativeType = string | boolean | number | UInt64 | Int64 | NativePointerValue;

type FieldContext = { offset: number; ptr: NativePointer; length: number };

// eslint-disable-next-line @typescript-eslint/no-explicit-any
type TypeInfo<T = any> = {
   size: (this: FieldContext) => number;
   get: (this: FieldContext) => NativeType;
   set: (this: FieldContext, value: T) => NativePointer;
};

const ScalarTypeMap = new Map<StructScalarField['name'], TypeInfo>([
   ['bool32', {
      size: () => 4,
      get() { return !!this.ptr.add(this.offset).readU32(); },
      set(value: boolean) { return this.ptr.add(this.offset).writeU32(value ? 1 : 0); }
   }],
   ['bool8', {
      size: () => 1,
      get() { return !!this.ptr.add(this.offset).readU8(); },
      set(value: boolean) { return this.ptr.add(this.offset).writeU8(value ? 1 : 0); }
   }],
   ['uint8', {
      size: () => 1,
      get() { return this.ptr.add(this.offset).readU8(); },
      set(value: number | UInt64) { return this.ptr.add(this.offset).writeU8(value); }
   }],
   ['int8', {
      size: () => 1,
      get() { return this.ptr.add(this.offset).readS8(); },
      set(value: number | Int64) { return this.ptr.add(this.offset).writeS8(value); }
   }],
   ['uint16', {
      size: () => 2,
      get() { return this.ptr.add(this.offset).readU16(); },
      set(value: number | UInt64) { return this.ptr.add(this.offset).writeU16(value); }
   }],
   ['int16', {
      size: () => 2,
      get() { return this.ptr.add(this.offset).readS16(); },
      set(value: number | Int64) { return this.ptr.add(this.offset).writeS16(value); }
   }],
   ['uint32', {
      size: () => 4,
      get() { return this.ptr.add(this.offset).readU32(); },
      set(value: number | UInt64) { return this.ptr.add(this.offset).writeU32(value); }
   }],
   ['int32', {
      size: () => 4,
      get() { return this.ptr.add(this.offset).readS32(); },
      set(value: number | Int64) { return this.ptr.add(this.offset).writeS32(value); }
   }],
   ['uint64', {
      size: () => 8,
      get() { return this.ptr.add(this.offset).readU64(); },
      set(value: number | UInt64) { return this.ptr.add(this.offset).writeU64(value); }
   }],
   ['int64', {
      size: () => 8,
      get() { return this.ptr.add(this.offset).readS64(); },
      set(value: number | Int64) { return this.ptr.add(this.offset).writeS64(value); }
   }],
   ['float', {
      size: () => 4,
      get() { return this.ptr.add(this.offset).readFloat(); },
      set(value: number) { return this.ptr.add(this.offset).writeFloat(value); }
   }],
   ['double', {
      size: () => 8,
      get() { return this.ptr.add(this.offset).readDouble(); },
      set(value: number) { return this.ptr.add(this.offset).writeDouble(value); }
   }],
   // this can be anything
   ['pointer', {
      size: () => Process.pointerSize,
      get() { return this.ptr.add(this.offset).readPointer(); },
      set(value: NativePointer) { return this.ptr.add(this.offset).writePointer(value); }
   }],
   // fixed-length string
   ['str', {
      size() { return this.length; },
      get() { return this.ptr.add(this.offset).readAnsiString(this.length); },
      set(value: string) { return this.ptr.add(this.offset).writeAnsiString(value); }
   }],
   ['ustr', {
      size() { return this.length; },
      get() { return this.ptr.add(this.offset).readUtf8String(this.length); },
      set(value: string) { return this.ptr.add(this.offset).writeUtf8String(value); }
   }],
   ['wstr', {
      size() { return this.length * 2; },
      get() { return this.ptr.add(this.offset).readUtf16String(this.length); },
      set(value: string) { return this.ptr.add(this.offset).writeUtf16String(value); }
   }],
]);

// I assume the runtime preserves the order of properties in object.
// Almost every Javascript Engines do this since ES2015.
type MemoryContext = {
   structPtr: NativePointer,
   offset: number,
}
function makeStructWrapper<T extends Struct>(memoryContext: MemoryContext, protoObj: T): [StructInstance<T>, number, number] {
   let maxAlignSize = 0;
   let totalSize = 0;
   let offset = memoryContext.offset;
   const wrapper = Object.entries(protoObj).reduce((wrapper, [fieldName, fieldInfo]) => {
      const typeInfo = ScalarTypeMap.get((fieldInfo as StructScalarField).name);
      if (typeInfo != null) {
         const scalarFieldInfo = fieldInfo as StructScalarField;
         const fieldContext: FieldContext = {
            offset: null,
            get ptr() { return memoryContext.structPtr; },
            length: scalarFieldInfo.name === 'str' || scalarFieldInfo.name === 'wstr' ? scalarFieldInfo.length : 0,
         };
         const size = typeInfo.size.call(fieldContext);
         const alignSize = size;
         const padding = (alignSize - totalSize % alignSize) % alignSize;
         totalSize += padding;
         memoryContext.offset += padding;
         fieldContext.offset = memoryContext.offset;
         Object.defineProperty(wrapper, fieldName, {
            enumerable: true,
            get: typeInfo.get.bind(fieldContext),
            set: typeInfo.set.bind(fieldContext),
         });
         if (maxAlignSize < alignSize)
            maxAlignSize = alignSize;
         totalSize += size;
         memoryContext.offset += size;
      }
      else if (Array.isArray(fieldInfo)) {         
         const sFieldInfo = fieldInfo[0];
         const length = fieldInfo[1];
         const sTypeInfo = ScalarTypeMap.get((sFieldInfo as StructScalarField).name);
         if (sTypeInfo != null) {
            const sScalarFieldInfo = sFieldInfo as StructScalarField;
            if (StringTypes.indexOf(sScalarFieldInfo.name) != -1)
               throw new Error('String types are not supported in array for now.')
            const alignSize = sTypeInfo.size.call(null);
            const size = length * alignSize;
            const padding = (alignSize - totalSize % alignSize) % alignSize;
            totalSize += padding;
            memoryContext.offset += padding;
            let array: any;
            Object.defineProperty(wrapper, fieldName, {
               enumerable: true,
               get: () => array ??= wrapArray(
                  sScalarFieldInfo.name, memoryContext.structPtr.add(memoryContext.offset), sTypeInfo, size),
            });
            if (maxAlignSize < alignSize)
               maxAlignSize = alignSize;
            totalSize += size;
            memoryContext.offset += size;
         }
         else if (Array.isArray(sFieldInfo)) {
            throw new Error("Nested array is not supported for now.");
         }
         else if (typeof (sFieldInfo) == 'object') {
            const structProto = sFieldInfo as Struct;
            const _offset = memoryContext.offset;
            const [, sAlignSize, elementSize] = makeStructWrapper(memoryContext, structProto); // probing call
            const size = length * elementSize;
            memoryContext.offset = _offset; // rollback offset
            const padding = (sAlignSize - totalSize % sAlignSize) % sAlignSize;
            totalSize += padding;
            memoryContext.offset += padding; // calculate the correct offset
            const sWrappers: StructInstance<Struct>[] = [];
            for (let i = 0; i < length; i++) {
               const [sWrapper] = makeStructWrapper(memoryContext, structProto); // real call
               sWrappers.push(sWrapper);
            }
            let array: any;
            Object.defineProperty(wrapper, fieldName, {
               enumerable: true,
               get: () => array ??= new StructArray(sWrappers),
            });
            if (maxAlignSize < sAlignSize)
               maxAlignSize = sAlignSize;
            totalSize += size;
         }
         else new Error(`Unknown struct's field type: [${fieldName}: "${fieldInfo}"]`);
      }
      else if (typeof (fieldInfo) === 'object') {
         const structProto = fieldInfo as Struct;
         const _offset = memoryContext.offset;
         const [, sAlignSize, sSize] = makeStructWrapper(memoryContext, structProto); // probing call
         memoryContext.offset = _offset; // rollback offset
         const padding = (sAlignSize - totalSize % sAlignSize) % sAlignSize;
         totalSize += padding;
         memoryContext.offset += padding; // calculate the correct offset
         const [sWrapper] = makeStructWrapper(memoryContext, structProto); // real call
         Object.defineProperty(wrapper, fieldName, {
            enumerable: true,
            get: () => sWrapper,
         });
         if (maxAlignSize < sAlignSize)
            maxAlignSize = sAlignSize;
         totalSize += sSize;
      }
      else new Error(`Unknown struct's field type: [${fieldName}: "${fieldInfo}"]`);
      return wrapper;
   }, {}) as any;
   const padding = (maxAlignSize - totalSize % maxAlignSize) % maxAlignSize;
   totalSize += padding;
   memoryContext.offset += padding;
   wrapper.getPtr = () => memoryContext.structPtr.add(offset);
   wrapper.getSize = () => totalSize;
   return [wrapper, maxAlignSize, totalSize];
}

export function createCStruct<T extends Struct>(protoObj: T, address?: NativePointer): StructInstance<T> {
   const memoryContext: MemoryContext = {
      structPtr: address,
      offset: 0,
   };
   const [wrapper, , size] = makeStructWrapper(memoryContext, protoObj);
   if (memoryContext.structPtr === undefined)
      memoryContext.structPtr = Memory.alloc(size);
   wrapper.setPtr = (ptr: NativePointer) => memoryContext.structPtr = ptr; 
   return wrapper;
}