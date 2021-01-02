
namespace Ben.Http
{
    internal readonly struct SocketFilter
    {
        readonly BpfFilter _code; // Filter code
        readonly byte _jt;        // Jump true
        readonly byte _jf;        // Jump false
        readonly uint _k;         // Multiuse

        public SocketFilter(BpfFilter code, byte jt, byte jf, uint k)
        {
            _code = code;
            _jt = jt;
            _jf = jf;
            _k = k;
        }
    };

    internal unsafe readonly struct SocketFilterProgram
    {
        readonly ushort len; /* Number of filter blocks */
        readonly SocketFilter* filter;

        public SocketFilterProgram(ushort Length, SocketFilter* Filter)
        {
            len = Length;
            filter = Filter;
        }
    };

    internal enum BpfFilter : ushort
    {
        BPF_LD		= 0x00,
        BPF_LDX		= 0x01,
        BPF_ST		= 0x02,
        BPF_STX		= 0x03,
        BPF_ALU		= 0x04,
        BPF_JMP		= 0x05,
        BPF_RET		= 0x06,
        BPF_MISC    = 0x07,

        /* ld/ldx fields */
        BPF_W		= 0x00,/* 32-bit */
        BPF_H		= 0x08,/* 16-bit */
        BPF_B		= 0x10,/*  8-bit */
        /* eBPF		BPF_DW		0x18    64-bit */

        BPF_IMM		= 0x00,
        BPF_ABS		= 0x20,
        BPF_IND		= 0x40,
        BPF_MEM		= 0x60,
        BPF_LEN		= 0x80,
        BPF_MSH		= 0xa0,

        /* alu/jmp fields */
        BPF_ADD		= 0x00,
        BPF_SUB		= 0x10,
        BPF_MUL		= 0x20,
        BPF_DIV		= 0x30,
        BPF_OR		= 0x40,
        BPF_AND		= 0x50,
        BPF_LSH		= 0x60,
        BPF_RSH		= 0x70,
        BPF_NEG		= 0x80,
        BPF_MOD		= 0x90,
        BPF_XOR		= 0xa0,

        BPF_JA		= 0x00,
        BPF_JEQ		= 0x10,
        BPF_JGT		= 0x20,
        BPF_JGE		= 0x30,
        BPF_JSET    = 0x40,
        BPF_K		= 0x00,
        BPF_X		= 0x08,

        /* ret - BPF_K and BPF_X also apply */
        //BPF_RVAL(code)  ((code) & 0x18)
        BPF_A       = 0x10,

        /* misc */
        //BPF_MISCOP(code) ((code) & 0xf8)
        BPF_TAX     = 0x00,
        BPF_TXA     = 0x80,



        /*
         * Number of scratch memory words for: BPF_ST and BPF_STX
         */
        BPF_MEMWORDS = 16,

        //BPF_NET_OFF	= SKF_NET_OFF,
        //BPF_LL_OFF	= SKF_LL_OFF,
    }

    enum SkfFilter : uint
    {

        /* RATIONALE. Negative offsets are invalid in BPF.
           We use them to reference ancillary data.
           Unlike introduction new instructions, it does not break
           existing compilers/optimizers.
         */
        SKF_AD_OFF = unchecked((ushort)-0x1_000),
        SKF_AD_PROTOCOL = 0,
        SKF_AD_PKTTYPE 	= 4,
        SKF_AD_IFINDEX 	= 8,
        SKF_AD_NLATTR	= 12,
        SKF_AD_NLATTR_NEST = 16,
        SKF_AD_MARK = 20,
        SKF_AD_QUEUE	= 24,
        SKF_AD_HATYPE	= 28,
        SKF_AD_RXHASH	= 32,
        SKF_AD_CPU = 36,
        SKF_AD_ALU_XOR_X = 40,
        SKF_AD_VLAN_TAG = 44,
        SKF_AD_VLAN_TAG_PRESENT = 48,
        SKF_AD_PAY_OFFSET = 52,
        SKF_AD_RANDOM = 56,
        SKF_AD_VLAN_TPID = 60,
        SKF_AD_MAX	= 64,

        SKF_NET_OFF	= unchecked((uint)-0x100_000),
        SKF_LL_OFF	= unchecked((uint)-0x200_000),

    }

    static class FilterExtensions
    {
        public static BpfFilter BPF_SRC(this BpfFilter code)
        {
            return (BpfFilter)((int)code & 0x08);
        }

        public static BpfFilter BPF_CLASS(this BpfFilter code)
        {
            return (BpfFilter)((int)code & 0x07);
        }

        public static BpfFilter BPF_SIZE(this BpfFilter code)
        {
            return (BpfFilter)((int)code & 0x18);
        }

        public static BpfFilter BPF_MODE(this BpfFilter code)
        {
            return (BpfFilter)((int)code & 0xe0);
        }

        public static BpfFilter BPF_OP(this BpfFilter code)
        {
            return (BpfFilter)((int)code & 0xf0);
        }
    }
}
