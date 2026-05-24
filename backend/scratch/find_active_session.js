const { PrismaClient } = require('@prisma/client');
const prisma = new PrismaClient();

async function main() {
  const member = await prisma.member.findFirst({ where: { memberType: 'VIP' } });
  if (!member) {
    console.log('No VIP member found');
    return;
  }

  // Find latest presence for this member
  const presenceLogs = await prisma.eventLog.findMany({
    where: {
      eventType: 'member.pc.presence',
    },
    orderBy: { createdAt: 'desc' },
    take: 100,
  });

  const activeLog = presenceLogs.find(log => {
    const payload = log.payload;
    return payload && payload.memberId === member.id && payload.isActive === true;
  });

  console.log('Active log:', activeLog);
}

main().catch(console.error).finally(() => prisma.$disconnect());
