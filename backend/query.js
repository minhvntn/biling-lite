const { PrismaClient } = require('@prisma/client');
const prisma = new PrismaClient();

async function run() {
  const settings = await prisma.appSetting.findMany();
  console.log('AppSettings:');
  settings.forEach(s => console.log(s.key, s.value));

  const groups = await prisma.pcGroup.findMany();
  console.log('Groups:');
  groups.forEach(g => console.log(g.name, g.hourlyRate, g.memberHourlyRate));
}

run().catch(console.error).finally(() => prisma.$disconnect());
