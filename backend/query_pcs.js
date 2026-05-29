const { PrismaClient } = require('@prisma/client');
const prisma = new PrismaClient();

async function main() {
  const sessions = await prisma.session.findMany({
    where: { status: 'ACTIVE' },
    include: { pc: true },
  });
  console.log('Active Sessions:', JSON.stringify(sessions, null, 2));

  const eventLogs = await prisma.eventLog.findMany({
    where: {
      eventType: 'member.pc.presence',
    },
    orderBy: { createdAt: 'desc' },
    take: 10,
  });
  console.log('Recent Member Presence Logs:', JSON.stringify(eventLogs, null, 2));

  const members = await prisma.member.findMany();
  console.log('All Members:', JSON.stringify(members.map(m => ({
    id: m.id,
    username: m.username,
    balance: m.balance,
    playSeconds: m.playSeconds,
    memberType: m.memberType
  })), null, 2));
}

main().finally(() => prisma.$disconnect());
